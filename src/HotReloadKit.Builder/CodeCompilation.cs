using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HotReloadKit.Builder;

public class CodeCompilation
{
    class HotReloadData
    {
        public string[] TypeNames { get; set; }
        public byte[] AssemblyData { get; set; }
        public byte[] PdbData { get; set; }
    }

    // -- static --

    static int asseblyVersion = 0;

    // -- public properties --

    public HotReloadRequest HotReloadRequest { get; set; }

    public Solution Solution { get; set; }
    public Project Project { get; set; }

    public string DllOutputhPath { get; set; }

    public IEnumerable<string> ChangedFilePaths { get; set; }

    // -- private --

    Compilation newCompilation;
    List<string> changedClassNames;

    // ----------------------------------
    // --------- compilation ------------

    public async Task Compile()
    {
        // ------ Microsoft.CodeAnalysis projects ------

        DllOutputhPath = DllOutputhPath ?? Project.OutputFilePath;

        var referencedProjects = Project.ProjectReferences?.Select(e => Solution.Projects.FirstOrDefault(x => x.Id == e.ProjectId));
        var generators = Project.AnalyzerReferences.SelectMany(e => e.GetGeneratorsForAllLanguages());
        var includedProjects = referencedProjects?.ToList() ?? new List<Microsoft.CodeAnalysis.Project>();
        includedProjects.Add(Project);

        var compilation = await Project?.GetCompilationAsync();

        // --------- syntax tree ----------

        List<SyntaxTree> syntaxTreeList = new List<SyntaxTree>();
        List<SyntaxTree> changedFilesSyntaxTreeList = new List<SyntaxTree>();

        // assembly name
        var versionSyntaxTree = CSharpSyntaxTree.ParseText($"[assembly: System.Reflection.AssemblyVersionAttribute(\"1.0.{asseblyVersion}\")]");

        // global usings
        var usings = compilation.SyntaxTrees
                .SelectMany(e => e
                    .GetRoot()
                    .DescendantNodes().OfType<UsingDirectiveSyntax>()
                    .Where(e => e.GlobalKeyword.ValueText == "global"))
                .Select(e => e.ToString())
                .Distinct()
                .ToList();
        if (usings.Count > 0)
        {
            var usingsText = string.Join("\n", usings);
            var usingsSyntaxTree = CSharpSyntaxTree.ParseText(usingsText);
            syntaxTreeList.Add(usingsSyntaxTree);
        }

        // collect changed and requested file paths
        var changedAndRequestedFilePaths = compilation.SyntaxTrees
            .Where(e =>
                !e.FilePath.EndsWith(".g.cs") &&
                (ChangedFilePaths.Contains(e.FilePath) ||
                GetClassNames(e).Intersect(HotReloadRequest.TypeNames).Count() > 0))
            .Select(e => e.FilePath)
            .ToList();

        // go through changed and requested files
        foreach (var filePath in changedAndRequestedFilePaths)
        {
            var codeText = await File.ReadAllTextAsync(filePath);
            var syntaxTree = CSharpSyntaxTree.ParseText(codeText);
            syntaxTreeList.Add(syntaxTree);
            changedFilesSyntaxTreeList.Add(syntaxTree);
        }

        changedClassNames = GetClassNamesForChangedSyntaxTrees(changedFilesSyntaxTreeList);

        // partial classes
        var partialSyntaxTrees = compilation.SyntaxTrees
                .Where(e =>
                    !e.FilePath.EndsWith(".g.cs") &&
                    GetClassNames(e).Intersect(changedClassNames).Count() > 0 &&
                    !changedAndRequestedFilePaths.Contains(e.FilePath));
        syntaxTreeList.AddRange(partialSyntaxTrees);

        // --------- metadata reference ---------
        List<MetadataReference> metadataReferences = new List<MetadataReference>();
        metadataReferences.AddRange(includedProjects.Select(e => MetadataReference.CreateFromFile(DllOutputhPath)));
        metadataReferences.AddRange(compilation.References);

        // --------- new compilation ------------

        var outputAssemblyName = $"{Project.AssemblyName}-{asseblyVersion}";

        CSharpCompilation newCompilationBeforeGenerators =
            CSharpCompilation.Create(outputAssemblyName, syntaxTreeList, metadataReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // --------- source genrators -----------

        var generatorDriver = CSharpGeneratorDriver.Create(generators);
        generatorDriver.RunGeneratorsAndUpdateCompilation(newCompilationBeforeGenerators, out newCompilation, out var diagnostics);
    }

    public async Task EmitJsonDataAsync(Func<string, Task> sendJsonData)
    {
        using (var dllStream = new MemoryStream())
        using (var pdbStream = new MemoryStream())
        {
            var emitResult = newCompilation.Emit(dllStream, pdbStream);
            if (emitResult.Success)
            {
                var hotReloadData = new HotReloadData
                {
                    TypeNames = changedClassNames.ToArray(),
                    AssemblyData = dllStream.GetBuffer(),
                    PdbData = pdbStream.GetBuffer()
                };

                var json = JsonSerializer.Serialize(hotReloadData);

                await sendJsonData(json);
            }
        }
    }

    // ------------------------------
    // --------- helpers ------------

    string GetClassNameWithNamespace(ClassDeclarationSyntax syntax)
    {
        var namespaceString =
                    (syntax.Parent as FileScopedNamespaceDeclarationSyntax)?.Name.ToString()
                    ?? (syntax.Parent as NamespaceDeclarationSyntax)?.Name.ToString();

        namespaceString = string.IsNullOrEmpty(namespaceString) ? "" : $"{namespaceString}.";

        return $"{namespaceString}{syntax.Identifier.Text}";
    }

    IEnumerable<string> GetClassNames(SyntaxTree syntaxTree)
    {
        return syntaxTree
            .GetRoot()
            .DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Where(e => e.Parent is NamespaceDeclarationSyntax || e.Parent is FileScopedNamespaceDeclarationSyntax)
            .Select(e => GetClassNameWithNamespace(e));
    }

    List<string> GetClassNamesForChangedSyntaxTrees(List<SyntaxTree> changedFilesSyntaxTreeList)
    {
        var classList = new List<string>();

        foreach (var syntaxTree in changedFilesSyntaxTreeList)
            classList.AddRange(GetClassNames(syntaxTree));

        return classList.Distinct().ToList();
    }
}

