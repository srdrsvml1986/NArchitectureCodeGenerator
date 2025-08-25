using System.Text.RegularExpressions;

namespace Core.CodeGen.Code.CSharp;

public static class CSharpCodeInjector
{
    public static async Task AddCodeLinesToMethodAsync(string filePath, string methodName, string[] codeLines)
    {
        var fileContent = (await System.IO.File.ReadAllLinesAsync(filePath)).ToList();
        string methodStartRegex =
            @"((public|protected|internal|protected internal|private protected|private)\s+)?(static\s+)?(void|[a-zA-Z]+(<.*>)?)\s+\b"
            + methodName
            + @"\b\s*\(";
        const string scopeBlockStartRegex = @"\{";
        const string scopeBlockEndRegex = @"\}";

        int methodStartIndex = -1;
        int methodEndIndex = -1;
        int curlyBracketCountInMethod = 1;
        for (int i = 0; i < fileContent.Count; ++i)
        {
            Match methodStart = Regex.Match(input: fileContent[i], methodStartRegex);
            if (!methodStart.Success)
                continue;

            methodStartIndex = i;
            if (!Regex.Match(input: fileContent[i], pattern: @"\{").Success)
                for (int j = methodStartIndex + 1; j < fileContent.Count; ++j)
                {
                    if (!Regex.Match(input: fileContent[j], pattern: @"\{").Success)
                        continue;
                    methodStartIndex = j;
                    break;
                }
        }

        for (int i = methodStartIndex + 1; i < fileContent.Count; ++i)
        {
            if (Regex.Match(input: fileContent[i], scopeBlockStartRegex).Success)
                ++curlyBracketCountInMethod;
            if (Regex.Match(input: fileContent[i], scopeBlockEndRegex).Success)
                --curlyBracketCountInMethod;
            if (curlyBracketCountInMethod != 0)
                continue;

            methodEndIndex = i;

            for (int j = methodEndIndex - 1; j > methodStartIndex; --j)
            {
                if (Regex.Match(input: fileContent[j], scopeBlockEndRegex).Success)
                    break;
                if (Regex.Match(input: fileContent[j], pattern: @"\)\s+return").Success)
                    break;
                if (
                    Regex.Match(input: fileContent[j], pattern: @"\s+return").Success
                    && Regex.Match(input: fileContent[j - 1], pattern: @"(if|else if|else)\s*\(").Success
                )
                    break;

                if (Regex.Match(input: fileContent[j], pattern: @"\s+return").Success)
                {
                    methodEndIndex = j;
                    break;
                }
            }

            break;
        }

        if (methodStartIndex == -1 || methodEndIndex == -1)
            throw new Exception($"{methodName} not found in \"{filePath}\".");

        ICollection<string> methodContent = fileContent.Skip(methodStartIndex + 1).Take(methodEndIndex - 1 - methodStartIndex).ToArray();
        int minimumSpaceCountInMethod =
            methodContent.Count < 2
                ? fileContent[methodStartIndex].TakeWhile(char.IsWhiteSpace).Count() * 2
                : methodContent.Where(line => !string.IsNullOrEmpty(line)).Min(line => line.TakeWhile(char.IsWhiteSpace).Count());

        fileContent.InsertRange(methodEndIndex, collection: codeLines.Select(line => new string(' ', minimumSpaceCountInMethod) + line));
        await System.IO.File.WriteAllLinesAsync(filePath, contents: fileContent.ToArray());
    }

    public static async Task AddCodeLinesAsPropertyAsync(
        string filePath,
        string[] codeLines,
        string? className = null // hedef sınıfı kilitlemek için
    )
    {
        var file = (await System.IO.File.ReadAllLinesAsync(filePath)).ToList();

        // 1) Hedef sınıf aralığını bul
        int classStart = -1, classOpenBrace = -1, classEnd = -1;

        Regex classStartRegex = className is null
            ? new Regex(@"\b(class|record)\s+\w[\w\<\>\,\s]*")
            : new Regex(@$"\b(class|record)\s+{Regex.Escape(className)}\b");

        Regex braceOpen = new(@"\{");
        Regex braceClose = new(@"\}");

        for (int i = 0; i < file.Count; i++)
        {
            if (!classStartRegex.IsMatch(file[i]))
                continue;
            classStart = i;

            // açılış süslüyü bul
            classOpenBrace = classStart;
            if (!braceOpen.IsMatch(file[classOpenBrace]))
            {
                for (int j = classStart + 1; j < file.Count; j++)
                {
                    if (braceOpen.IsMatch(file[j]))
                    { classOpenBrace = j; break; }
                }
            }

            // eşleşen kapanışı sayarak bul
            int depth = 1;
            for (int j = classOpenBrace + 1; j < file.Count; j++)
            {
                if (braceOpen.IsMatch(file[j]))
                    depth++;
                if (braceClose.IsMatch(file[j]))
                    depth--;
                if (depth == 0)
                { classEnd = j; break; }
            }
            break; // ilk (ya da ismen eşleşen) sınıfı al
        }

        if (classStart == -1 || classOpenBrace == -1 || classEnd == -1)
            throw new Exception($"Target class {(className ?? "(first)")} not found in \"{filePath}\".");

        // 2) Hedef sınıf gövdesinde, derinlik 0 seviyesindeki son property’yi bul
        // Auto-property deseni (get; set;) – sade ve güvenli
        Regex propertyRegex = new(@"^\s*(public|protected|internal|private|protected\s+internal|private\s+protected)\s+[\w\.\<\>\?\[\],\s]+\s+\w+\s*\{\s*get;\s*set;\s*\}\s*$");
        Regex nestedClassAtLevel0Regex = new(@"\b(class|record)\s+\w");

        int insertAfter = classOpenBrace; // varsayılan: açılış brace’inin hemen altı
        int localDepth = 0;

        for (int i = classOpenBrace + 1; i < classEnd; i++)
        {
            if (braceOpen.IsMatch(file[i]))
                localDepth++;
            if (braceClose.IsMatch(file[i]))
                localDepth--;

            // nested class başlıyorsa ve henüz property bulmadıysak,
            // nested class’tan önce eklemek adına durumu koru
            if (localDepth == 0 && nestedClassAtLevel0Regex.IsMatch(file[i]))
                break;

            if (localDepth == 0 && propertyRegex.IsMatch(file[i]))
                insertAfter = i; // sınıf seviyesindeki son property
        }

        // 3) İndent hesapla
        int baseIndent =
            (from k in Enumerable.Range(classOpenBrace + 1, Math.Max(0, insertAfter - classOpenBrace))
             let line = file[k]
             where line.Trim().Length > 0
             select line.TakeWhile(char.IsWhiteSpace).Count())
            .DefaultIfEmpty(file[classStart].TakeWhile(char.IsWhiteSpace).Count() + 4)
            .Min();

        // 4) Tekrarlı eklemeyi önle
        var trimmedTargets = codeLines.Select(l => l.Trim()).ToHashSet();
        bool alreadyExists = file.Skip(classOpenBrace + 1).Take(classEnd - classOpenBrace - 1)
                                 .Any(l => trimmedTargets.Contains(l.Trim()));
        if (alreadyExists)
            return;

        // 5) Ekle
        var prefixed = codeLines.Select(l => new string(' ', baseIndent) + l).ToList();
        file.InsertRange(insertAfter + 1, prefixed);

        await System.IO.File.WriteAllLinesAsync(filePath, file);
    }


    public static async Task AddCodeLinesToRegionAsync(string filePath, IEnumerable<string> linesToAdd, string regionName)
    {
        var fileContent = (await System.IO.File.ReadAllLinesAsync(filePath)).ToList();
        string regionStartRegex = @$"^\s*#region\s*{regionName}\s*";
        const string regionEndRegex = @"^\s*#endregion\s*.*";

        bool isInRegion = false;
        int indexToAdd;
        for (indexToAdd = 0; indexToAdd < fileContent.Count; indexToAdd++)
        {
            string fileLine = fileContent[indexToAdd];

            if (Regex.Match(fileLine, regionStartRegex).Success)
            {
                isInRegion = true;
                continue;
            }

            if (!isInRegion)
                continue;
            if (!Regex.Match(fileLine, regionEndRegex).Success)
                continue;

            int minimumSpaceCountInRegion = fileContent[indexToAdd].TakeWhile(char.IsWhiteSpace).Count();

            string previousLine = fileContent[index: indexToAdd - 1];
            if (Regex.Match(previousLine, regionStartRegex).Success)
            {
                fileContent.Insert(indexToAdd, string.Empty);
                indexToAdd += 2;
            }

            if (!string.IsNullOrEmpty(previousLine))
                fileContent.Insert(index: indexToAdd, string.Empty);

            fileContent.InsertRange(
                index: indexToAdd,
                collection: linesToAdd.Select(line => new string(' ', minimumSpaceCountInRegion) + line)
            );
            await System.IO.File.WriteAllLinesAsync(filePath, fileContent);
            break;
        }
    }

    public static async Task AddUsingToFile(string filePath, IEnumerable<string> usingLines)
    {
        var fileContent = (await System.IO.File.ReadAllLinesAsync(filePath)).ToList();

        IEnumerable<string> usingLinesToAdd = usingLines.Where(usingLine => !fileContent.Contains(usingLine));

        Regex usingRegex = new(@"^using\s+.*;$");
        int indexToAdd = 0;
        for (int i = 0; i < fileContent.Count; ++i)
        {
            string fileLine = fileContent[i];
            if (usingRegex.IsMatch(fileLine))
                continue;
            indexToAdd = i;
            break;
        }

        fileContent.InsertRange(indexToAdd, usingLinesToAdd);
        await System.IO.File.WriteAllLinesAsync(filePath, fileContent);
    }

    public static async Task AddMethodToClass(string filePath, string className, string[] codeLines)
    {
        var fileContent = (await System.IO.File.ReadAllLinesAsync(filePath)).ToList();
        Regex classStartRegex =
            new(@$"((public|protected|internal|protected internal|private protected|private)\s+)?(static\s+)?\s+\b{className}");
        Regex scopeBlockStartRegex = new(@"\{");
        Regex scopeBlockEndRegex = new(@"\}");

        int classStartIndex = -1;
        int classEndIndex = -1;
        for (int i = 0; i < fileContent.Count; ++i)
        {
            string fileLine = fileContent[i];

            Match methodStart = classStartRegex.Match(input: fileLine);
            if (!methodStart.Success)
                continue;

            classStartIndex = i;
            if (!scopeBlockStartRegex.Match(fileLine).Success)
                for (int j = classStartIndex + 1; j < fileContent.Count; ++j)
                {
                    if (!scopeBlockStartRegex.Match(fileContent[j]).Success)
                        continue;
                    classStartIndex = j;
                    break;
                }
            break;
        }

        int curlyBracketCountInClass = 1;
        for (int i = classStartIndex + 1; i < fileContent.Count; ++i)
        {
            if (scopeBlockStartRegex.Match(input: fileContent[i]).Success)
                ++curlyBracketCountInClass;
            if (scopeBlockEndRegex.Match(input: fileContent[i]).Success)
                --curlyBracketCountInClass;
            if (curlyBracketCountInClass != 0)
                continue;

            classEndIndex = i;
            break;
        }
        if (classStartIndex == -1 || classEndIndex == -1)
            throw new Exception($"{className} not found in \"{filePath}\".");

        ICollection<string> classContent = fileContent.Skip(classStartIndex + 1).Take(classEndIndex - 1 - classStartIndex).ToArray();

        int minimumSpaceCountInClass =
            classContent.Count < 2
                ? fileContent[classStartIndex].TakeWhile(char.IsWhiteSpace).Count() * 2
                : classContent.Where(line => !string.IsNullOrEmpty(line)).Min(line => line.TakeWhile(char.IsWhiteSpace).Count());

        fileContent.InsertRange(classEndIndex, collection: codeLines.Select(line => new string(' ', minimumSpaceCountInClass) + line));
        await System.IO.File.WriteAllLinesAsync(filePath, contents: fileContent.ToArray());
    }
}
