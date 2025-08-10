using System.Text.RegularExpressions;
using Application.Features.Generate.Constants;
using Core.CodeGen.File;
using Core.CrossCuttingConcerns.Exceptions;
using Core.CrossCuttingConcerns.Helpers;
using Domain.Constants;
using System.Threading.Tasks;
using System.IO;
using System.Linq;

namespace Application.Features.Generate.Rules;

public class GenerateBusinessRules
{
    public async Task EntityClassShouldBeInhreitEntityBaseClass(string projectPath, string entityName)
    {
        string entityFilePath = PlatformHelper.SecuredPathJoin(
            projectPath, "Domain", "Entities", $"{entityName}.cs"
        );

        string fileContent = await File.ReadAllTextAsync(entityFilePath);

        // Daha esnek regex patternleri
        var patterns = new[]
        {
            $@"public\s+(partial\s+)?class\s+{entityName}\s*:\s*.*\bEntity\b.*", // Standart Entity
            $@"public\s+(partial\s+)?class\s+{entityName}\s*:\s*.*\bLog\b.*",    // Log Entity
            $@"public\s+(partial\s+)?class\s+{entityName}\s*:\s*.*\bExceptionLog\b.*" // ExceptionLog
        };

        bool isInheritingEntityBase = patterns.Any(pattern =>
            Regex.IsMatch(fileContent, pattern, RegexOptions.Singleline));

        // Namespace kontrolü (daha esnek)
        bool hasEntityNamespace =
            fileContent.Contains("Core.Persistence.Repositories") ||
            fileContent.Contains("Core.Security.Entities") ||
            fileContent.Contains("Core.Security.Entities.Logs");

        if (!isInheritingEntityBase || !hasEntityNamespace)
        {
            throw new BusinessException(
                GenerateBusinessMessages.EntityClassShouldBeInheritEntityBaseClass(entityName)
            );
        }
    }

    public Task FileShouldNotBeExists(string filePath)
    {
        if (File.Exists(filePath))
            throw new BusinessException(GenerateBusinessMessages.FileAlreadyExists(filePath));
        return Task.CompletedTask;
    }
}