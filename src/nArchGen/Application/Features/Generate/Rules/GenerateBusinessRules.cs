using System.Text.RegularExpressions;
using Application.Features.Generate.Constants;
using Core.CodeGen.File;
using Core.CrossCuttingConcerns.Exceptions;
using Core.CrossCuttingConcerns.Helpers;
using Domain.Constants;

namespace Application.Features.Generate.Rules;

public class GenerateBusinessRules
{
    public async Task EntityClassShouldBeInhreitEntityBaseClass(string projectPath, string entityName)
    {
        string filePath = Path.Combine(projectPath, "Domain", "Entities", $"{entityName}.cs");
        string[] fileContent = await File.ReadAllLinesAsync(filePath);

        // Geliştirilmiş kalıtım kontrolü
        bool isValidInheritance = fileContent.Any(line =>
            line.Contains(": Entity<") ||                     // Temel Entity kalıtımı
            line.Contains(": IEntity") ||                     // Arayüz implementasyonu
            line.Contains("Core.Security.Entities.Log<") ||   // Log özel durumu
            line.Contains("Core.Security.Entities.ExceptionLog<") ||   // Log özel durumu
            line.Contains("Core.Security.Entities.DeviceToken<") ||   // Log özel durumu
            line.Contains($"class {entityName} : Entity")     // Temel Entity
        );

        if (!isValidInheritance)
        {
            throw new BusinessException(GenerateBusinessMessages.EntityClassShouldBeInheritEntityBaseClass(entityName));
        }
    }

    public Task FileShouldNotBeExists(string filePath)
    {
        if (Directory.Exists(filePath))
            throw new BusinessException(GenerateBusinessMessages.FileAlreadyExists(filePath));
        return Task.CompletedTask;
    }
}