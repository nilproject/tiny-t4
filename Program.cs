using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace tiny_t4
{
    class Program
    {
        private class TransformResult
        {
            public string Code { get; }
            public KeyValuePair<string, string>[] Directives { get; }

            public TransformResult(string code, KeyValuePair<string, string>[] directives)
            {
                Code = code ?? throw new ArgumentNullException(nameof(code));
                Directives = directives ?? throw new ArgumentNullException(nameof(directives));
            }
        }

        static void Main(string[] args)
        {
            var forceRebgen = args.Contains("--force");
            var toStdOut = false;
            string[] files = null;
            files = args.Where(x => !x.StartsWith("--")).ToArray();
            if (files.Length > 0)
            {                
                toStdOut = true;

                Console.WriteLine("Files to process: " + string.Join(Environment.NewLine, files));
            }
            else
            {
                Console.WriteLine("Current directory: " + Directory.GetCurrentDirectory());
                files = Directory.EnumerateFiles(".", "*.tt", SearchOption.AllDirectories).ToArray();

                Console.WriteLine("Found files:" + Environment.NewLine + string.Join(Environment.NewLine, files));
                Console.WriteLine();
            }

            var originalConsoleOutput = new StreamWriter(Console.OpenStandardOutput());

            foreach (var file in files)
            {
                var targetExtension = "cs";
                var targetFileName = Path.ChangeExtension(file, targetExtension);

                if (File.GetLastWriteTime(file) < File.GetLastWriteTime(targetFileName) && !forceRebgen)
                {
                    originalConsoleOutput.WriteLine("Skipped: " + file);
                    originalConsoleOutput.Flush();
                    continue;
                }

                var fileProcessingStopWatch = Stopwatch.StartNew();
                originalConsoleOutput.WriteLine("Processing: " + file);
                originalConsoleOutput.Flush();

                var code = File.ReadAllText(file);
                var transformResult = transform(code);

                var scriptOptions = ScriptOptions.Default;
                scriptOptions = scriptOptions.AddReferences(Assembly.GetExecutingAssembly());
                foreach (var directive in transformResult.Directives)
                {
                    switch (directive.Key)
                    {
                        case "import namespace":
                        {
                            scriptOptions = scriptOptions.AddImports(directive.Value);
                            break;
                        }
                    }
                }

                StreamWriter writter = null;
                if (!toStdOut)
                {
                    writter = new StreamWriter(new FileStream(targetFileName, FileMode.Create));
                    Console.SetOut(writter);
                }

                code = transformResult.Code;
                code = $"static readonly tiny_t4.{nameof(Host)} Host = new tiny_t4.{nameof(Host)}(@\"{Path.GetFullPath(file)}\");\n" + code;

                try
                {
                    var script = CSharpScript.Create(code, scriptOptions);
                    script.Compile();
                    script.CreateDelegate().Invoke().ContinueWith((result) =>
                    {
                        if (!result.IsCompletedSuccessfully)
                        {
                            Console.Error.WriteLine(result.Exception);
                        }
                        else
                        {
                            Console.WriteLine(result.Result);
                        }
                    })
                    .Wait();

                    originalConsoleOutput.WriteLine("Done: " + file + ". Elapsed: " + fileProcessingStopWatch.Elapsed);
                    originalConsoleOutput.Flush();
                }
                catch (Exception e)
                {
                    var innerException = e.InnerException;
                    while (innerException != null)
                    {
                        Console.Error.WriteLine(innerException.GetType().Name);
                        Console.Error.WriteLine(innerException.StackTrace);
                        Console.Error.WriteLine(innerException.Message);
                        innerException = e.InnerException;
                    }

                    File.WriteAllText(Path.ChangeExtension(file, "debug_error." + targetExtension), code);

                    Console.WriteLine("Error: " + e.Message);

                    throw;
                }
                finally
                {
                    if (!toStdOut)
                    {
                        writter.Dispose();
                    }
                }
            }
        }

        private static TransformResult transform(string code)
        {
            var parts = ("#>" + code).Split("<#");
            var output = new StringBuilder();
            var directives = new List<KeyValuePair<string, string>>();
            var textBegun = false;
            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                var endOfPart = part.IndexOf("#>");

                if (endOfPart == -1)
                    throw new ArgumentException(nameof(code));

                var partCode = part.Substring(0, endOfPart);
                var partText = part.Substring(endOfPart + 2);

                if (partCode.Length > 0)
                {
                    if (partCode[0] == '@')
                    {
                        var directive = partCode.Substring(1).Trim();
                        var valuePos = directive.IndexOf("=");
                        var value = directive.Substring(valuePos + 1);
                        directive = directive.Substring(0, valuePos);

                        if (value[0] == '"')
                            value = value.Trim('"');

                        directives.Add(new KeyValuePair<string, string>(directive, value));
                    }
                    else if (partCode[0] == '=')
                    {
                        output.Append("System.Console.Write(" + partCode.Substring(1).Trim() + ");");
                    }
                    else
                    {
                        output.Append(partCode);
                    }
                }

                if (textBegun ? partText.Length > 0 : !string.IsNullOrWhiteSpace(partText))
                {
                    textBegun = true;
                    output.AppendLine("System.Console.Write(@\"" + partText.Replace("\"", "\"\"") + "\");");
                }
            }

            return new TransformResult(output.ToString(), directives.ToArray());
        }
    }
}
