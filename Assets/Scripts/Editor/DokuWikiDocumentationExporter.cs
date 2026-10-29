// Place in Assets/Editor/DokuWikiDocumentationExporter.cs
// No third-party Unity packages required.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;

public class DokuWikiDocumentationExporter : EditorWindow
{
    private readonly List<string> paths = new List<string>();
    private Vector2 scroll;
    private string preview = "";
    private bool includeUndocumented = true;
    private bool includePrivate = false;
    private bool includeDiagram = true;
    private bool oneCombinedPage = true;

    [MenuItem("Tools/Documentation/DokuWiki Exporter")]
    private static void Open() => GetWindow<DokuWikiDocumentationExporter>("DokuWiki Exporter");

    [MenuItem("Assets/Documentation/Add to DokuWiki Exporter", false, 2000)]
    private static void AddFromContext()
    {
        Open();
        var window = GetWindow<DokuWikiDocumentationExporter>();
        window.AddSelection();
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox("Select .cs files or folders in the Project window, then click Add Selection. Generate DokuWiki text containing Mermaid UML class diagrams.", MessageType.Info);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Add Selection")) AddSelection();
            if (GUILayout.Button("Clear")) { paths.Clear(); preview = ""; }
        }
        EditorGUILayout.LabelField($"Files: {paths.Count}", EditorStyles.boldLabel);
        using (var view = new EditorGUILayout.ScrollViewScope(scroll, GUILayout.Height(110)))
        {
            scroll = view.scrollPosition;
            foreach (var path in paths) EditorGUILayout.LabelField(path, EditorStyles.miniLabel);
        }
        includeDiagram = EditorGUILayout.ToggleLeft("Include Mermaid class diagram", includeDiagram);
        includeUndocumented = EditorGUILayout.ToggleLeft("Include undocumented members", includeUndocumented);
        includePrivate = EditorGUILayout.ToggleLeft("Include private members", includePrivate);
        oneCombinedPage = EditorGUILayout.ToggleLeft("Combine into one wiki page", oneCombinedPage);
        using (new EditorGUI.DisabledScope(paths.Count == 0))
        {
            if (GUILayout.Button("Generate Preview")) Generate();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Copy to Clipboard"))
                {
                    Generate();
                    EditorGUIUtility.systemCopyBuffer = preview;
                    ShowNotification(new GUIContent("Copied DokuWiki markup"));
                }
                if (GUILayout.Button("Export .txt File(s)")) Export();
            }
        }
        EditorGUILayout.LabelField("DokuWiki markup preview", EditorStyles.boldLabel);
        using (var view = new EditorGUILayout.ScrollViewScope(Vector2.zero))
        {
            EditorGUILayout.TextArea(preview, GUILayout.ExpandHeight(true));
        }
    }

    private void AddSelection()
    {
        foreach (var item in Selection.objects)
        {
            var path = AssetDatabase.GetAssetPath(item);
            if (string.IsNullOrEmpty(path)) continue;
            if (AssetDatabase.IsValidFolder(path))
            {
                foreach (var guid in AssetDatabase.FindAssets("t:MonoScript", new[] { path }))
                    AddPath(AssetDatabase.GUIDToAssetPath(guid));
            }
            else AddPath(path);
        }
        paths.Sort(StringComparer.OrdinalIgnoreCase);
        Repaint();
    }

    private void AddPath(string path)
    {
        if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && !paths.Contains(path))
            paths.Add(path);
    }

    private List<ScriptInfo> ParseAll()
    {
        var scripts = new List<ScriptInfo>();
        foreach (var path in paths)
        {
            try { scripts.Add(SourceParser.Parse(path)); }
            catch (Exception ex) { Debug.LogWarning($"Unable to parse {path}: {ex.Message}"); }
        }
        return scripts;
    }

    private void Generate()
    {
        preview = WikiWriter.Write(ParseAll(), includeDiagram, includeUndocumented, includePrivate);
    }

    private void Export()
    {
        var scripts = ParseAll();
        if (scripts.Count == 0) return;
        if (oneCombinedPage)
        {
            var file = EditorUtility.SaveFilePanel("Save DokuWiki page", "", "unity-api", "txt");
            if (string.IsNullOrEmpty(file)) return;
            File.WriteAllText(file, WikiWriter.Write(scripts, includeDiagram, includeUndocumented, includePrivate), new UTF8Encoding(false));
        }
        else
        {
            var folder = EditorUtility.OpenFolderPanel("Export DokuWiki pages", "", "");
            if (string.IsNullOrEmpty(folder)) return;
            foreach (var script in scripts)
            {
                var name = Regex.Replace(Path.GetFileNameWithoutExtension(script.Path), "[^a-zA-Z0-9_-]", "_");
                File.WriteAllText(Path.Combine(folder, name + ".txt"),
                    WikiWriter.Write(new List<ScriptInfo> { script }, includeDiagram, includeUndocumented, includePrivate), new UTF8Encoding(false));
            }
        }
        ShowNotification(new GUIContent("Documentation exported"));
    }

    private sealed class ScriptInfo
    {
        public string Path;
        public readonly List<TypeInfo> Types = new List<TypeInfo>();
    }
    private sealed class TypeInfo
    {
        public string Name, Kind, BaseList, Summary, Remarks;
        public readonly List<MemberInfo> Members = new List<MemberInfo>();
    }
    private sealed class MemberInfo
    {
        public string Name, Signature, Summary, Remarks, Returns, Visibility;
        public bool IsMethod;
        public readonly List<KeyValuePair<string, string>> Parameters = new List<KeyValuePair<string, string>>();
    }

    // Intentionally dependency-free, best-effort parser for conventional C# declarations.
    // This is NOT a full C# syntax parser (see usage notes below).
    private static class SourceParser
    {
        private static readonly Regex TypePattern = new Regex(@"\b(class|struct|interface|enum)\s+(\w+)(?:\s*<[^>{}]+>)?\s*(?::\s*([^\{]+))?", RegexOptions.Compiled);
        private static readonly Regex Modifier = new Regex(@"\b(public|protected|internal|private)\b", RegexOptions.Compiled);
        private static readonly Regex DeclarationHint = new Regex(@"\b(class|struct|interface|enum)\b|\(|\{|;|=>", RegexOptions.Compiled);

        public static ScriptInfo Parse(string assetPath)
        {
            var script = new ScriptInfo { Path = assetPath };
            var source = File.ReadAllText(Path.GetFullPath(assetPath));
            // Strip block comments without removing newlines, but keep XML documentation lines.
            source = Regex.Replace(source, @"/\*(?!\*)([\s\S]*?)\*/", m => new string('\n', m.Value.Count(c => c == '\n')));
            var lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var doc = new List<string>();
            TypeInfo current = null;
            int depth = 0, typeDepth = -1;
            bool pendingTypeBrace = false;
            var declaration = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("///", StringComparison.Ordinal))
                {
                    doc.Add(line.Substring(3).Trim());
                    continue;
                }
                if (line.StartsWith("[", StringComparison.Ordinal) && !line.Contains(";")) continue; // Attributes
                if (line.Length == 0) continue;
                if (line.StartsWith("//", StringComparison.Ordinal)) continue;
                if (line.StartsWith("#", StringComparison.Ordinal)) continue;

                // Collect multi-line declarations, including signatures with parameters on separate lines.
                if (doc.Count > 0 || declaration.Length > 0 || TypePattern.IsMatch(line) ||
                    (current != null && depth == typeDepth &&
                     Regex.IsMatch(line, @"^(public|protected|internal|private|static|readonly|virtual|override|abstract|sealed|async)\b")))
                {
                    declaration.Append(' ').Append(line);
                    string candidate = declaration.ToString().Trim();
                    bool completed = DeclarationHint.IsMatch(candidate) &&
                        (candidate.Contains("{") || candidate.Contains(";") || candidate.Contains("=>")) &&
                        (!candidate.Contains("(") || candidate.Contains(")"));
                    if (completed)
                    {
                        string summary, remarks, returns;
                        var parameters = new List<KeyValuePair<string, string>>();
                        ReadXml(doc, out summary, out remarks, out returns, parameters);
                        var typeMatch = TypePattern.Match(candidate);
                        if (typeMatch.Success)
                        {
                            current = new TypeInfo {
                                Kind = typeMatch.Groups[1].Value,
                                Name = typeMatch.Groups[2].Value,
                                BaseList = (typeMatch.Groups[3].Value ?? "").Trim().Split('{')[0].Trim(),
                                Summary = summary, Remarks = remarks
                            };
                            script.Types.Add(current);
                            typeDepth = depth + CountBraces(line);
                            pendingTypeBrace = !candidate.Contains("{");
                        }
                        else if (current != null && (depth == typeDepth || pendingTypeBrace) &&
                            !candidate.StartsWith("using ") && !candidate.StartsWith("namespace "))
                        {
                            var member = ParseMember(candidate, summary, remarks, returns, parameters);
                            if (member != null) current.Members.Add(member);
                        }
                        doc.Clear();
                        declaration.Length = 0;
                    }
                    else if (declaration.Length > 3000) { declaration.Length = 0; doc.Clear(); }
                }
                depth += CountBraces(line);
                if (pendingTypeBrace && line.Contains("{")) { typeDepth = depth; pendingTypeBrace = false; }
                if (current != null && !pendingTypeBrace && depth < typeDepth)
                {
                    current = null;
                    typeDepth = -1;
                }
                // When a doc comment begins directly before a declaration, the parser above records it.
            }
            return script;
        }

        private static int CountBraces(string line)
        {
            // Ignore braces in simple string literals and line comments.
            line = Regex.Replace(line, "@?\"(?:\"\"|\\\\.|[^\"])*\"", "\"\"");
            int comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) line = line.Substring(0, comment);
            return line.Count(c => c == '{') - line.Count(c => c == '}');
        }

        private static MemberInfo ParseMember(string declaration, string summary, string remarks, string returns,
            List<KeyValuePair<string, string>> parameters)
        {
            var head = declaration.Split(new[] { "=>", "{", ";" }, StringSplitOptions.None)[0].Trim();
            if (head.StartsWith("return ") || head.StartsWith("if ") || head.StartsWith("for ")) return null;
            var member = new MemberInfo { Signature = head, Summary = summary, Remarks = remarks, Returns = returns };
            var visibility = Modifier.Match(head);
            member.Visibility = visibility.Success ? visibility.Value : "private";
            member.IsMethod = head.Contains("(") && head.Contains(")");
            var before = member.IsMethod ? head.Substring(0, head.IndexOf('(')).Trim() : head.Split('=')[0].Trim();
            var match = Regex.Match(before, @"([A-Za-z_][A-Za-z_0-9]*)\s*$");
            if (!match.Success) return null;
            member.Name = match.Groups[1].Value;
            member.Parameters.AddRange(parameters);
            return member;
        }

        private static void ReadXml(List<string> lines, out string summary, out string remarks, out string returns,
            List<KeyValuePair<string, string>> parameters)
        {
            summary = remarks = returns = "";
            if (lines.Count == 0) return;
            try
            {
                var xml = XDocument.Parse("<root>" + string.Join("\n", lines) + "</root>");
                summary = Plain(xml.Root.Element("summary"));
                remarks = Plain(xml.Root.Element("remarks"));
                returns = Plain(xml.Root.Element("returns"));
                foreach (var element in xml.Root.Elements("param"))
                    parameters.Add(new KeyValuePair<string, string>((string)element.Attribute("name") ?? "", Plain(element)));
            }
            catch { summary = Regex.Replace(string.Join(" ", lines), "<[^>]*>", "").Trim(); }
        }
        private static string Plain(XElement x) => x == null ? "" : Regex.Replace(x.Value, @"\s+", " ").Trim();
    }

    private static class WikiWriter
    {
        public static string Write(List<ScriptInfo> scripts, bool diagram, bool undocumented, bool privateMembers)
        {
            var sb = new StringBuilder();
            sb.AppendLine("====== Unity API Documentation ======").AppendLine();
            sb.AppendLine("Generated from C# source documentation comments.\n");
            var types = scripts.SelectMany(s => s.Types).ToList();
            if (diagram && types.Count > 0) WriteDiagram(sb, types, privateMembers);
            foreach (var script in scripts)
            {
                if (script.Types.Count == 0)
                {
                    sb.AppendLine("<tab><tab><fc:red>No documented type declarations detected.</fc>\n");
                    continue;
                }
                foreach (var type in script.Types)
                {
                    sb.AppendLine("<WRAP codedoc-heading>");
                    sb.AppendLine("<wrap codedoc-type>" + Safe(type.Kind.ToUpper()) + "</wrap>");
                    sb.AppendLine(Safe(type.Name));
                    sb.AppendLine(" <wrap codedoc-source>" + Safe(script.Path) + "</wrap>");
                    if (!string.IsNullOrEmpty(type.BaseList))
                        sb.AppendLine(" <wrap codedoc-inheritance>" + Safe(type.BaseList) + "</wrap>");
                    sb.AppendLine("</WRAP>");
                    
                    if (!string.IsNullOrEmpty(type.Summary))
                        sb.AppendLine("<WRAP codedoc-summary>" + Safe(type.Summary) + "</WRAP>");
                    
                    if (!string.IsNullOrEmpty(type.Remarks))
                        sb.AppendLine("\n**Remarks:** " + Safe(type.Remarks));
                    sb.AppendLine();
                    var members = type.Members.Where(m => (undocumented || !string.IsNullOrEmpty(m.Summary)) &&
                        (privateMembers || m.Visibility != "private")).ToList();
                    foreach (var m in members)
                    {
                        sb.AppendLine("<code csharp>\n" + m.Signature.Replace("</code>", "") + "\n</code>");
                        if (!string.IsNullOrEmpty(m.Summary)) sb.AppendLine(Safe(m.Summary));
                        foreach (var p in m.Parameters)
                            sb.AppendLine("  * **" + Safe(p.Key) + "** — " + Safe(p.Value));
                        if (!string.IsNullOrEmpty(m.Returns)) sb.AppendLine("**Returns:** " + Safe(m.Returns));
                        if (!string.IsNullOrEmpty(m.Remarks)) sb.AppendLine("**Remarks:** " + Safe(m.Remarks));
                        sb.AppendLine();
                    }
                }
            }
            return sb.ToString().TrimEnd() + "\n";
        }

        private static void WriteDiagram(StringBuilder sb, List<TypeInfo> types, bool includePrivate)
        {
            sb.AppendLine("===== Class diagram =====\n");
            
            sb.AppendLine("<mermaid>");
            sb.AppendLine("%%{init: {'theme': 'base', 'themeVariables': {");
            sb.AppendLine("  'primaryColor': '#1e293b',");
            sb.AppendLine("  'primaryTextColor': '#f8fafc',");
            sb.AppendLine("  'primaryBorderColor': '#64748b',");
            sb.AppendLine("  'lineColor': '#94a3b8',");
            sb.AppendLine("  'background': '#0f172a'");
            sb.AppendLine("}}}%%");
            sb.AppendLine("classDiagram\n    direction TB");
            var known = new HashSet<string>(types.Select(t => t.Name));
            foreach (var t in types)
            {
                var id = Id(t.Name);
                sb.AppendLine("    class " + id + " {");
                if (t.Kind == "interface") sb.AppendLine("        <<interface>>");
                foreach (var m in t.Members.Where(m => includePrivate || m.Visibility != "private").Take(25))
                {
                    string symbol = m.Visibility == "public" ? "+" : m.Visibility == "protected" ? "#" : m.Visibility == "private" ? "-" : "~";
                    sb.AppendLine("        " + symbol + Id(m.Name) + (m.IsMethod ? "()" : ""));
                }
                sb.AppendLine("    }");
            }
            var edges = new HashSet<string>();
            foreach (var t in types)
            {
                if (string.IsNullOrEmpty(t.BaseList)) continue;
                foreach (var rawBase in t.BaseList.Split(','))
                {
                    var baseName = Regex.Match(rawBase.Trim(), @"[A-Za-z_][A-Za-z_0-9]*").Value;
                    if (baseName.Length == 0 || baseName == t.Name) continue;
                    // Only link types in the selected set and Unity's two common base classes.
                    if (!known.Contains(baseName) && baseName != "MonoBehaviour" && baseName != "ScriptableObject") continue;
                    var edge = "    " + Id(baseName) + " <|-- " + Id(t.Name);
                    if (edges.Add(edge)) sb.AppendLine(edge);
                }
            }
            sb.AppendLine("</mermaid>\n");
        }
        private static string Id(string s) => Regex.Replace(s, @"[^A-Za-z0-9_]", "_");
        private static string Safe(string s) => (s ?? "").Replace("**", "\\*\\*").Replace("//", "\\/\\/").Replace("[[", "\\[\\[").Replace("{{", "\\{\\{").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
