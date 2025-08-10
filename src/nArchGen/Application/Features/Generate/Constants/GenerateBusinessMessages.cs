namespace Application.Features.Generate.Constants;

public static class GenerateBusinessMessages
{
    public static string EntityClassShouldBeInheritEntityBaseClass(string entityName) =>
    $"Hata: {entityName} sınıfı, doğrudan veya dolaylı olarak Entity taban sınıfından türemelidir. " +
    "Örnek: 'public class Log : Core.Security.Entities.Logs.Log<Guid, Guid>'";
    public static string FileAlreadyExists(string path) => $"File already exists in path: {path}";
}
