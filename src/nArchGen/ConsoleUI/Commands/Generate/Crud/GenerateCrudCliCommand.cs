using Application.Features.Generate.Commands.Crud;
using Core.CodeGen.Code.CSharp;
using Core.CodeGen.Code.CSharp.ValueObjects;
using Core.CrossCuttingConcerns.Helpers;
using Domain.ValueObjects;
using MediatR;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Reflection;
using System.Text.RegularExpressions;
using PropertyInfo = Core.CodeGen.Code.CSharp.ValueObjects.PropertyInfo;

namespace ConsoleUI.Commands.Generate.Crud;

public partial class GenerateCrudCliCommand : AsyncCommand<GenerateCrudCliCommand.Settings>
{
    private readonly IMediator _mediator;

    public GenerateCrudCliCommand(IMediator mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(paramName: nameof(mediator));
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        settings.CheckProjectName();
        settings.CheckEntityArgument();
        settings.CheckDbContextArgument();
        settings.CheckMechanismOptions();

        string entityPath = PlatformHelper.SecuredPathJoin(settings.ProjectPath, "Domain", "Entities", $"{settings.EntityName}.cs");
        ICollection<PropertyInfo> entityProperties = await CSharpCodeReader.ReadClassPropertiesAsync(entityPath, settings.ProjectPath);
        string entityIdType = (await CSharpCodeReader.ReadBaseClassGenericArgumentsAsync(entityPath)).First();
        entityProperties = await AddBasePropertiesIfNeeded(entityPath, settings.ProjectPath, entityProperties);

        GenerateCrudCommand generateCrudCommand =
            new()
            {
                CrudTemplateData = new CrudTemplateData
                {
                    Entity = new Entity
                    {
                        Name = settings.EntityName!,
                        IdType = entityIdType,
                        Properties = entityProperties.Where(property => property.AccessModifier == "public").ToArray()
                    },
                    IsCachingUsed = settings.IsCachingUsed,
                    IsLoggingUsed = settings.IsLoggingUsed,
                    IsTransactionUsed = settings.IsTransactionUsed,
                    IsSecuredOperationUsed = settings.IsSecuredOperationUsed,
                    DbContextName = settings.DbContextName!
                },
                ProjectPath = settings.ProjectPath,
                DbContextName = settings.DbContextName!
            };

        IAsyncEnumerable<GeneratedCrudResponse> resultsStream = _mediator.CreateStream(request: generateCrudCommand);

        await AnsiConsole
            .Status()
            .Spinner(Spinner.Known.Dots2)
            .SpinnerStyle(style: Style.Parse(text: "blue"))
            .StartAsync(
                status: "Generating...",
                action: async ctx =>
                {
                    await foreach (GeneratedCrudResponse result in resultsStream)
                    {
                        ctx.Status(result.CurrentStatusMessage);

                        if (result.OutputMessage is not null)
                            AnsiConsole.MarkupLine(result.OutputMessage);

                        if (result.LastOperationMessage is not null)
                            AnsiConsole.MarkupLine($":check_mark_button: {result.LastOperationMessage}");

                        if (result.NewFilePathsResult is not null)
                        {
                            AnsiConsole.MarkupLine(":new_button: [green]Generated files:[/]");
                            foreach (string filePath in result.NewFilePathsResult)
                                AnsiConsole.Write(new TextPath(filePath).StemColor(Color.Yellow).LeafColor(Color.Blue));
                        }

                        if (result.UpdatedFilePathsResult is not null)
                        {
                            AnsiConsole.MarkupLine(":up_button: [green]Updated files:[/]");
                            foreach (string filePath in result.UpdatedFilePathsResult)
                                AnsiConsole.Write(new TextPath(filePath).StemColor(Color.Yellow).LeafColor(Color.Blue));
                        }
                    }
                }
            );

        return 0;
    }

    private static async Task<ICollection<PropertyInfo>>
AddBasePropertiesIfNeeded(
    string entityPath,
    string projectPath,
    ICollection<PropertyInfo> entityProperties)
    {
        // 1) Dosyayı oku
        string text = await System.IO.File.ReadAllTextAsync(entityPath);
        // Sadece class tanım satırını yakalamak yeterli
        string? classLine = text.Split(Environment.NewLine)
                                .FirstOrDefault(l => l.Contains(" class ") && l.Contains(":"));
        if (classLine == null)
            return entityProperties;

        // 2) Base type Core.Security.Entities mi? (tam nitelikli veya using ile)
        var usings = await CSharpCodeReader.ReadUsingNameSpacesAsync(entityPath); // :contentReference[oaicite:1]{index=1}
        bool hasCoreUsing = usings.Any(u => u.Trim().Equals("NArchitectureTemplate.Core.Security.Entities", StringComparison.Ordinal));
        bool inheritsFromCoreFqn = classLine.Contains("NArchitectureTemplate.Core.Security.Entities.");
        if (!inheritsFromCoreFqn && !hasCoreUsing)
            return entityProperties;

        // 3) Base class adını (simple) yakala
        // Örn: "public class Role : NArchitectureTemplate.Core.Security.Entities.Role<int>"
        //  veya: "public class Group : Role<int>"  (using ile)
        string baseSimpleName = null!;
        var mFqn = System.Text.RegularExpressions.Regex.Match(
            classLine,
            @"Core\.Security\.Entities\.(\w+)\s*<"  // tam nitelikli
        );
        if (mFqn.Success)
            baseSimpleName = mFqn.Groups[1].Value;
        else
        {
            var mUsing = System.Text.RegularExpressions.Regex.Match(
                classLine,
                @"class\s+\w+\s*:\s*(\w+)\s*<"       // using ile kısaltılmış
            );
            if (mUsing.Success && hasCoreUsing)
                baseSimpleName = mUsing.Groups[1].Value;
        }

        if (string.IsNullOrWhiteSpace(baseSimpleName))
            return entityProperties; // Core.Security değil ya da yakalanamadı

        // 4) Generic argümanları Domain dosyasından al (örn. ["int"], ["Guid","Guid"])
        var genericArgsText = await Core.CodeGen.Code.CSharp.CSharpCodeReader
                                   .ReadBaseClassGenericArgumentsAsync(entityPath); // :contentReference[oaicite:2]{index=2}
        var typeArgs = genericArgsText.Select(MapToClrType).ToArray();
        if (typeArgs.Length == 0)
            return entityProperties; // generic değilse veya okunamadıysa dokunma

        // 5) Core.Security.Entities assembly’sini bul veya yükle
        var coreAsm = TryGetCoreSecurityAssembly(projectPath);
        if (coreAsm == null)
            return entityProperties;

        // 6) Açık generic base tipini bul (Role`1, DeviceToken`2, Group`1 …)
        //    GetTypes() bazı ortamlarda patlayabilir; DefinedTypes + try/catch ile güvenli ilerle.
        Type? openGeneric = null;
        try
        {
            openGeneric = coreAsm.DefinedTypes
                .FirstOrDefault(ti =>
                    ti.Namespace == "NArchitectureTemplate.Core.Security.Entities" &&
                    ti.Name.StartsWith(baseSimpleName + "`", StringComparison.Ordinal))
                ?.AsType();
        }
        catch (System.Reflection.ReflectionTypeLoadException ex)
        {
            openGeneric = ex.Types?.FirstOrDefault(t =>
                t?.Namespace == "NArchitectureTemplate.Core.Security.Entities" &&
                t.Name.StartsWith(baseSimpleName + "`", StringComparison.Ordinal));
        }
        if (openGeneric == null || !openGeneric.IsGenericTypeDefinition)
            return entityProperties;

        // 7) Kapalı türü inşa et (Role<int> gibi)
        Type closed = openGeneric.MakeGenericType(typeArgs);

        // 8) Kapalı türün public instance property’lerini al
        var closedProps = closed.GetProperties(System.Reflection.BindingFlags.Public |
                                               System.Reflection.BindingFlags.Instance);

        // 9) Mevcut liste ile çakışma önleme + Id’yi ekleme (Create/Update şablonları için daha doğru)
        var existingNames = new HashSet<string>(entityProperties.Select(p => p.Name));
        foreach (var p in closedProps)
        {
            if (p.Name == "Id")
                continue; // Id'yi şablon zaten ayrı ele alıyor
            if (existingNames.Contains(p.Name))
                continue;

            var pt = p.PropertyType;
            entityProperties.Add(new Core.CodeGen.Code.CSharp.ValueObjects.PropertyInfo
            {
                AccessModifier = "public",
                Name = p.Name,
                Type = ToCSharpTypeName(pt), // string, Guid, int, DateTime?, vb. doğru yazım
                NameSpace = GetNamespaceIfNeeded(pt)
            });
        }

        return entityProperties;
    }

    // ----- yardımcılar -----

    private static Type MapToClrType(string typeName)
    {
        // Nullable yok; çünkü generic argümanlarda nullable beklemiyoruz (Role<int>, DeviceToken<Guid,Guid> gibi)
        return typeName switch
        {
            "string" or "String" => typeof(string),
            "int" or "Int32" => typeof(int),
            "long" or "Int64" => typeof(long),
            "short" or "Int16" => typeof(short),
            "byte" or "Byte" => typeof(byte),
            "bool" or "Boolean" => typeof(bool),
            "decimal" or "Decimal" => typeof(decimal),
            "double" or "Double" => typeof(double),
            "float" or "Single" => typeof(float),
            "Guid" => typeof(Guid),
            "DateTime" => typeof(DateTime),
            _ => Type.GetType(typeName, throwOnError: false) ?? typeof(object) // son çare
        };
    }

    private static string ToCSharpTypeName(Type t)
    {
        // Nullable<T> -> T?
        if (Nullable.GetUnderlyingType(t) is Type ut)
            return ToCSharpTypeName(ut) + "?";

        if (t == typeof(string))
            return "string";
        if (t == typeof(int))
            return "int";
        if (t == typeof(long))
            return "long";
        if (t == typeof(short))
            return "short";
        if (t == typeof(byte))
            return "byte";
        if (t == typeof(bool))
            return "bool";
        if (t == typeof(decimal))
            return "decimal";
        if (t == typeof(double))
            return "double";
        if (t == typeof(float))
            return "float";
        if (t == typeof(Guid))
            return "Guid";
        if (t == typeof(DateTime))
            return "DateTime";

        // Koleksiyon/generic’lere şimdilik ihtiyaç yok; gerekirse genişletilir.
        return t.Name;
    }

    private static string? GetNamespaceIfNeeded(Type t)
    {
        // built-in’ler için namespace döndürme
        if (t.IsGenericType && Nullable.GetUnderlyingType(t) is Type ut)
            t = ut;

        if (t == typeof(string) || t.IsPrimitive)
            return null;
        if (t == typeof(Guid) || t == typeof(DateTime))
            return "System";

        return t.Namespace; // gerekirse
    }

    private static System.Reflection.Assembly? TryGetCoreSecurityAssembly(string projectPath)
    {
        // 1) AppDomain’de yüklü mü?
        var asm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name?.Contains("NArchitectureTemplate.Core", StringComparison.OrdinalIgnoreCase) == true &&
                                 a.DefinedTypes.Any(ti => ti.Namespace == "NArchitectureTemplate.Core.Security.Entities"));
        if (asm != null)
            return asm;

        // 2) Fallback: diskten yükle (yol projene göre ayarlanabilir)

        string coreDllPath = Path.Combine(
    Path.GetDirectoryName(projectPath), // GenerateCrudCommand içindeki ProjectPath
    "Core", "Core.Security", "bin", "Debug", "net9.0", "Core.Security.dll");

        if (System.IO.File.Exists(coreDllPath))
            try
            { return System.Reflection.Assembly.LoadFrom(coreDllPath); }
            catch { /* ignore */ }

        return null;
    }

    //private static async Task<ICollection<PropertyInfo>> AddBasePropertiesIfNeeded(
    //    string entityPath,
    //    ICollection<PropertyInfo> entityProperties, Settings settings)
    //{
    //    var text = await File.ReadAllTextAsync(entityPath);

    //    // class tanımı satırını bul
    //    var classLine = text.Split(Environment.NewLine)
    //                        .FirstOrDefault(l => l.Contains("class "));

    //    if (classLine is null)
    //        return entityProperties;

    //    // eğer Core.Security.Entities.* altında bir base class varsa
    //    var match = Regex.Match(classLine, @"Core\.Security\.Entities\.([A-Za-z0-9_`]+)");
    //    if (!match.Success)
    //        return entityProperties;

    //    var baseClassName = match.Groups[1].Value; // ör: DeviceToken, Group

    //    string coreDllPath = Path.Combine(
    //        Path.GetDirectoryName(settings.ProjectPath), // GenerateCrudCommand içindeki ProjectPath
    //        "Core","Core.Security", "bin", "Debug", "net9.0", "Core.Security.dll"
    //    );

    //    Assembly assembly;
    //    if (File.Exists(coreDllPath))
    //    {
    //        assembly = Assembly.LoadFrom(coreDllPath);
    //    }
    //    else
    //    {
    //        // Fallback: AppDomain'de ara
    //        assembly = AppDomain.CurrentDomain.GetAssemblies()
    //            .FirstOrDefault(a => a.GetTypes().Any(t =>
    //                t.FullName != null &&
    //                t.FullName.StartsWith("Core.Security.Entities")));
    //    }

    //    if (assembly == null)
    //        throw new InvalidOperationException("Core.Security.Entities assembly could not be loaded.");


    //    Type? baseType = null;

    //    try
    //    {
    //        baseType = assembly.GetTypes()
    //            .FirstOrDefault(t => t.Name.StartsWith(baseClassName));
    //    }
    //    catch (ReflectionTypeLoadException ex)
    //    {
    //        var safeTypes = ex.Types.Where(t => t != null);
    //        baseType = safeTypes.FirstOrDefault(t => t!.Name.StartsWith(baseClassName));
    //    }


    //    if (baseType is null)
    //        return entityProperties;

    //    // base class’ın public property’lerini al
    //    var baseProperties = baseType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
    //        .Where(p => p.CanRead && p.CanWrite) // sadece okunabilir-yazılabilir
    //        .Select(p => new PropertyInfo
    //        {
    //            Name = p.Name,
    //            Type = getTypeName(p.PropertyType),
    //            AccessModifier = "public",
    //            NameSpace = p.PropertyType.Namespace
    //        });

    //    // var olanlarla çakışmayanları ekle
    //    var existing = new HashSet<string>(entityProperties.Select(p => p.Name));
    //    foreach (var bp in baseProperties)
    //        if (!existing.Contains(bp.Name))
    //            entityProperties.Add(bp);

    //    return entityProperties;
    //}

    //private static string getTypeName(Type type)
    //{
    //    if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
    //        return $"{Nullable.GetUnderlyingType(type).Name}?";

    //    return type.Name switch
    //    {
    //        "String" => "string",
    //        "Int32" => "int",
    //        "Int64" => "long",
    //        "Boolean" => "bool",
    //        "Guid" => "Guid",
    //        "DateTime" => "DateTime",
    //        _ => type.Name
    //    };
    //}



}
