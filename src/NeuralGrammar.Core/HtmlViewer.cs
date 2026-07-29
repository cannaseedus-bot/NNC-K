#nullable enable
using System;
using System.IO;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// Generates HTML/JS wrappers for K'UHUL program visualization
    /// in browser-based modals. The PowerShell UI creates the WPF window
    /// and WebBrowser control; this class provides the content.
    /// </summary>
    public static class HtmlViewer
    {
        /// <summary>Read a local HTML file and return its content.</summary>
        public static string LoadFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path is required", nameof(filePath));
            if (!File.Exists(filePath))
                throw new FileNotFoundException("HTML file not found", filePath);
            return File.ReadAllText(filePath);
        }

        /// <summary>
        /// Generate an HTML page that loads and runs a .kuhul program
        /// using the kuhul-runtime.js in the same directory.
        /// </summary>
        public static string WrapProgram(
            string kuhulPath,
            string? jsRuntimePath = null)
        {
            if (string.IsNullOrWhiteSpace(kuhulPath))
                throw new ArgumentException("K'UHUL program path is required", nameof(kuhulPath));
            if (!File.Exists(kuhulPath))
                throw new FileNotFoundException("K'UHUL program not found", kuhulPath);

            var programJson = File.ReadAllText(kuhulPath);
            var dir = Path.GetDirectoryName(kuhulPath) ?? ".";

            var runtimePath = jsRuntimePath ?? Path.Combine(dir, "kuhul-runtime.js");
            var runtimeJs = File.Exists(runtimePath)
                ? File.ReadAllText(runtimePath)
                : "// kuhul-runtime.js not found";

            var programName = Path.GetFileNameWithoutExtension(kuhulPath);

            return "<!DOCTYPE html>\n<html>\n<head>\n" +
                "<meta charset='utf-8'>\n" +
                "<meta name='viewport' content='width=device-width,initial-scale=1'>\n" +
                "<style>\n" +
                "  * { margin:0; padding:0; box-sizing:border-box; }\n" +
                "  body { background:#0d1117; color:#e2e8f0; font:13px/1.5 Consolas,sans-serif; padding:16px; }\n" +
                "  #trace { background:#161b22; border:1px solid #30363d; border-radius:6px; padding:12px; margin-bottom:12px; min-height:60px; font-size:11px; color:#8b949e; overflow-y:auto; }\n" +
                "  #output { background:#0d1117; border:1px solid #30363d; border-radius:6px; padding:16px; min-height:200px; white-space:pre-wrap; }\n" +
                "  .fold { color:#58a6ff; font-weight:bold; }\n" +
                "  .mem { color:#7ee787; }\n" +
                "  .tool { color:#d2a8ff; }\n" +
                "</style>\n" +
                "<script>\n" + runtimeJs + "\n" +
                "window.onload = function() {\n" +
                "  if (typeof KuhulRuntime !== 'undefined') {\n" +
                "    var prog = " + programJson + ";\n" +
                "    KuhulRuntime.load(prog).then(function(p) {\n" +
                "      document.getElementById('trace').innerHTML = '<div class=\"fold\">Loaded: ' + (p.meta ? p.meta.name : 'program') + '</div>';\n" +
                "      var result = KuhulRuntime.run({ question: 'input' });\n" +
                "      document.getElementById('output').textContent = JSON.stringify(result, null, 2);\n" +
                "    });\n" +
                "  }\n" +
                "};\n" +
                "</script>\n" +
                "</head>\n<body>\n" +
                "<h2 style='color:#58a6ff;margin-bottom:8px;'>" + programName + "</h2>\n" +
                "<div id='trace'>Loading...</div>\n" +
                "<div id='output'></div>\n" +
                "</body>\n</html>";
        }

        /// <summary>
        /// Generate a complete PWA-style app: index.html with embedded
        /// kuhul program + runtime. Returns the HTML string.
        /// </summary>
        public static string WrapApp(
            string kuhulJson,
            string runtimeJs,
            string appName)
        {
            return "<!DOCTYPE html>\n<html>\n<head>\n" +
                "<meta charset='utf-8'>\n" +
                "<meta name='viewport' content='width=device-width,initial-scale=1'>\n" +
                "<link rel='manifest' href='manifest.kuhul'>\n" +
                "<title>" + appName + "</title>\n" +
                "<style>\n" +
                "  * { margin:0; padding:0; box-sizing:border-box; }\n" +
                "  body { background:#0d1117; color:#e2e8f0; font:13px/1.5 Consolas,sans-serif; padding:16px; }\n" +
                "  #fold-trace { background:#161b22; border:1px solid #30363d; border-radius:6px; padding:12px; font-size:11px; color:#8b949e; overflow-y:auto; max-height:200px; margin-bottom:12px; }\n" +
                "  #app-output { padding:16px; white-space:pre-wrap; }\n" +
                "</style>\n" +
                "<script>\n" + runtimeJs + "\n" +
                "window.onload = function() {\n" +
                "  if (typeof KuhulRuntime !== 'undefined') {\n" +
                "    var prog = " + kuhulJson + ";\n" +
                "    KuhulRuntime.load(prog).then(function(p) {\n" +
                "      var ft = document.getElementById('fold-trace');\n" +
                "      ft.innerHTML = '<div>Loaded: ' + (p.meta ? p.meta.name : 'program') + '</div>';\n" +
                "      var result = KuhulRuntime.run({});\n" +
                "      document.getElementById('app-output').textContent = JSON.stringify(result, null, 2);\n" +
                "    });\n" +
                "  }\n" +
                "};\n" +
                "</script>\n" +
                "</head>\n<body>\n" +
                "<h2 style='color:#58a6ff;'>" + appName + "</h2>\n" +
                "<div id='fold-trace'></div>\n" +
                "<div id='app-output'></div>\n" +
                "</body>\n</html>";
        }
    }
}
