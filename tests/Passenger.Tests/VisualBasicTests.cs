using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.Workspace;
using Passenger.Editor;
using Passenger.Languages;
using Passenger.Lsp;
using Passenger.Text;

namespace Passenger.Tests;

public sealed class VisualBasicTests : FileColoursFolder, IClassFixture<VisualBasicTests.Project>
{
    private readonly Project project;

    public VisualBasicTests(Project project)
    {
        this.project = project;
        project.Reset();
    }

    [AvaloniaTheory]
    [InlineData("Sample.vb")]
    [InlineData("Sample.VB")]
    public void Visual_Basic_has_colours_and_apostrophe_comments_without_a_server(string name)
    {
        MainWindow window = Window();
        const string Source = "Public Class Sample\n    ' A comment\n    Public Function Value() As String\n        Return \"hello\"\n    End Function\nEnd Class\n";
        CodeEditor editor = Open(window, name, Source);
        Assert.Same(Language.VisualBasic, window.EditorArea.Active!.Language);
        Assert.Equal(TokenKind.Keyword, KindOf(editor, "Public"));
        Assert.Equal(TokenKind.Keyword, KindOf(editor, "Return"));
        Assert.Equal(TokenKind.String, KindOf(editor, "\"hello\""));
        Assert.Equal(TokenKind.Comment, KindOf(editor, "' A comment"));
        Assert.Equal(LanguageService.CSharp, Language.VisualBasic.Service);
        Ui.PlaceAfter(editor, "Return");
        Ui.Run(window, EditorActions.ToggleLineComment);
        Assert.Contains("        ' Return \"hello\"", editor.Document.Snapshot.ToString(), StringComparison.Ordinal);
        Ui.Run(window, EditorActions.ToggleLineComment);
        Assert.Equal(Source, editor.Document.Snapshot.ToString());
    }

    [AvaloniaFact]
    public void Visual_Basic_completes_reports_errors_navigates_and_renames_across_a_CSharp_reference()
    {
        MainWindow window = project.Open("Library/Greeter.vb");
        OpenDocument greeter = window.EditorArea.Active!;
        Ui.WaitFor(() => window.CSharpService?.For(greeter)?.Lsp is not null, "the Visual Basic file connected to Roslyn");
        Assert.Equal(LanguageIds.VisualBasic, window.CSharpService!.For(greeter)!.Lsp!.LanguageId);

        window.OpenFile(project.PathOf("Library/Caller.vb"));
        OpenDocument caller = window.EditorArea.Active!;
        CodeEditor editor = window.EditorArea.CodeEditor;
        Ui.PlaceAfter(editor, "Greeter.");
        Ui.Run(window, AppActions.Complete);
        Ui.WaitFor(() => window.Assist.CompletionLabels.Contains("Hello"),
            () => $"Visual Basic member completion; got {string.Join(", ", window.Assist.CompletionLabels)}");
        Ui.Press(window, "ESCAPE");
        Ui.PlaceAfter(editor, "Greeter.He");
        Ui.Run(window, AppActions.GoToDeclaration);
        Ui.WaitFor(() => window.EditorArea.Active == greeter, "the declaration in Greeter.vb");

        window.OpenFile(project.PathOf("App/Program.cs"));
        OpenDocument program = window.EditorArea.Active!;
        Ui.PlaceAfter(window.EditorArea.CodeEditor, "Greeter.He");
        Ui.Run(window, AppActions.GoToDeclaration);
        Ui.WaitFor(() => window.EditorArea.Active == greeter, "the Visual Basic declaration from C#");
        Ui.Run(window, AppActions.Rename);
        Ui.WaitFor(() => window.Assist.Renaming == "Hello", "rename on a Visual Basic method");
        window.KeyTextInput("Welcome");
        Ui.Press(window, "ENTER");
        Ui.WaitFor(() => program.Text.Snapshot.ToString().Contains("Greeter.Welcome()", StringComparison.Ordinal)
            && caller.Text.Snapshot.ToString().Contains("Greeter.Welcome()", StringComparison.Ordinal), "the VB and C# callers renamed together");
        Assert.Contains("Function Welcome()", greeter.Text.Snapshot.ToString(), StringComparison.Ordinal);
        Ui.WaitFor(() => greeter.Markers.Any(m => m.Kind == MarkerKind.Error && m.Message.Contains("BC30451", StringComparison.Ordinal)),
            () => $"the Visual Basic diagnostic for the missing name; got {string.Join(", ", greeter.Markers.Select(m => m.Message))}");
        editor = window.EditorArea.CodeEditor;
        int returned = editor.Document.Snapshot.ToString().IndexOf("Return Missing", StringComparison.Ordinal);
        editor.ApplyEdits([new TextEdit(returned, "Return Missing".Length, "Return    \"hello\"")]);
        Ui.Run(window, AppActions.Reformat);
        Ui.WaitFor(() => greeter.Text.Snapshot.ToString().Contains("Return \"hello\"", StringComparison.Ordinal), "Visual Basic formatting");
        Ui.WaitFor(() => !greeter.Markers.Any(m => m.Kind == MarkerKind.Error), "the diagnostic cleared after the missing name was fixed");
    }

    public sealed class Project : SharedProject
    {
        public Project()
            : base("visual-basic-")
        {
            Directory.CreateDirectory(PathOf("Library"));
            Directory.CreateDirectory(PathOf("App"));
            File.WriteAllText(PathOf("Mixed.slnx"), "<Solution><Project Path=\"Library/Library.vbproj\" /><Project Path=\"App/App.csproj\" /></Solution>");
            File.WriteAllText(PathOf("Library/Library.vbproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework><RootNamespace>Library</RootNamespace></PropertyGroup></Project>");
            File.WriteAllText(PathOf("Library/Greeter.vb"),
                "Public Class Greeter\n    Public Shared Function Hello() As String\n        Return Missing\n    End Function\nEnd Class\n");
            File.WriteAllText(PathOf("Library/Caller.vb"),
                "Public Class Caller\n    Public Function Say() As String\n        Return Greeter.Hello()\n    End Function\nEnd Class\n");
            File.WriteAllText(PathOf("App/App.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net11.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include=\"../Library/Library.vbproj\" /></ItemGroup></Project>");
            File.WriteAllText(PathOf("App/Program.cs"), "System.Console.WriteLine(Library.Greeter.Hello());\n");
        }
    }
}
