using System.Diagnostics;
using System.Reflection;
using System.Text;
using ImageMagick;

namespace _pdfImageExtractor;

class Program
{
    private readonly static string AppDirectoryPath = Path.GetDirectoryName (Assembly.GetExecutingAssembly ().Location)!;

    private readonly static string ParametersFilePath = Path.Join (AppDirectoryPath, "Parameters.txt");

    private readonly static Lazy <IEnumerable <string>> _parameters = new (() =>
    {
        if (File.Exists (ParametersFilePath))
            return File.ReadAllLines (ParametersFilePath, Encoding.UTF8).
                Select (x => x.Trim ()).
                Where (y => string.IsNullOrEmpty (y) == false && y.StartsWith ("//") == false);

        return [];
    });

    private static IEnumerable <string> Parameters => _parameters.Value;

    private readonly static string LogsDirectoryPath = Path.Join (AppDirectoryPath, "Logs");

    private readonly static string LogsFilePath = Path.Join (LogsDirectoryPath, DateTime.UtcNow.ToString ("yyyyMMdd'T'HHmmss'Z.log'"));

    private readonly static Lazy <StreamWriter> _logsWriter = new (() =>
    {
        Directory.CreateDirectory (LogsDirectoryPath);
        return new (LogsFilePath, append: true, Encoding.UTF8);
    });

    private static StreamWriter LogsWriter => _logsWriter.Value;

    private static void WriteLineToConsole (string message)
    {
        Console.WriteLine (message);
        LogsWriter.WriteLine (message);
    }

    private static void WriteLineToConsole (string message, ConsoleColor backgroundColor, ConsoleColor? foregroundColor = null)
    {
        Console.BackgroundColor = backgroundColor;

        if (foregroundColor.HasValue)
            Console.ForegroundColor = foregroundColor.Value;

        else
        {
            switch (backgroundColor)
            {
                case ConsoleColor.Red:
                    Console.ForegroundColor = ConsoleColor.White;
                    break;

                default:
                    break;
            }
        }

        WriteLineToConsole (message);

        Console.ResetColor ();
    }

    static void Main (string [] args)
    {
        try
        {
            if (Parameters.Any () == false && File.Exists (ParametersFilePath) == false)
            {
                File.WriteAllLines (ParametersFilePath,
                [
                    "pdfimages_exe_path: ",
                    "reextract_images: false",
                    "",
                    "// Directory #1",
                    "source_directory_path: ",
                    "dest_directory_path: ",
                    "excluded_file_name: ",
                    "",
                    "// Directory #2",
                    "source_directory_path: ",
                    "dest_directory_path: "
                ],
                Encoding.UTF8);

                WriteLineToConsole ("Parameters file created.");
                return;
            }

            string xPdfImagesExePath = Parameters.Single (x => x.StartsWith ("pdfimages_exe_path:"));
            xPdfImagesExePath = xPdfImagesExePath.Substring (xPdfImagesExePath.IndexOf (':') + 1).Trim ();

            if (File.Exists (xPdfImagesExePath) == false)
            {
                WriteLineToConsole ("pdfimages.exe not found.");
                return;
            }

            string xReextractImagesString = Parameters.Single (x => x.StartsWith ("reextract_images:"));
            bool xReextractImages = bool.Parse (xReextractImagesString.Substring (xReextractImagesString.IndexOf (':') + 1).Trim ());

            string? xSourceDirectoryPath = null,
                    xDestDirectoryPath = null;

            List <string> xExcludedFileNames = [];

            void _extractImages (string sourceDirectoryPath, string? destDirectoryPath, IEnumerable <string> excludedFileNames)
            {
                if (Directory.Exists (sourceDirectoryPath) == false)
                {
                    WriteLineToConsole ($"Source directory not found: {sourceDirectoryPath}");
                    return;
                }

                if (string.IsNullOrEmpty (destDirectoryPath) || Path.IsPathFullyQualified (destDirectoryPath) == false)
                {
                    WriteLineToConsole ($"Invalid dest directory path: {destDirectoryPath}");
                    return;
                }

                if (excludedFileNames.Any (x => string.IsNullOrEmpty (x)))
                {
                    WriteLineToConsole ("At least one excluded file name is empty.");
                    return;
                }

                WriteLineToConsole ("Source directory: " + sourceDirectoryPath);
                WriteLineToConsole ("Dest directory: " + destDirectoryPath);

                if (excludedFileNames.Any ())
                    WriteLineToConsole ("Excluded file names: " + string.Join (", ", excludedFileNames));

                if (Directory.Exists (destDirectoryPath) && xReextractImages)
                {
                    try
                    {
                        Directory.Delete (destDirectoryPath, recursive: true);
                    }

                    catch
                    {
                        WriteLineToConsole ($"Failed to delete: {destDirectoryPath}", ConsoleColor.Red);
                        return;
                    }
                }

                Directory.CreateDirectory (destDirectoryPath);

                foreach (string xPdfFilePath in Directory.GetFiles (sourceDirectoryPath, "*.pdf", SearchOption.TopDirectoryOnly))
                {
                    string xPdfFileName = Path.GetFileName (xPdfFilePath);

                    if (excludedFileNames.Contains (xPdfFileName, StringComparer.OrdinalIgnoreCase))
                    {
                        WriteLineToConsole ($"Images extraction skipped for: {xPdfFileName}");
                        continue;
                    }

                    string xDestSubdirectoryPath = Path.Join (destDirectoryPath, Path.GetFileNameWithoutExtension (xPdfFilePath));

                    if (Directory.Exists (xDestSubdirectoryPath))
                    {
                        WriteLineToConsole ($"Images already extracted for: {xPdfFileName}");
                        continue;
                    }

                    WriteLineToConsole ($"Extracting images for: {xPdfFileName}");

                    Directory.CreateDirectory (xDestSubdirectoryPath);

                    using Process xProcess = new ()
                    {
                        StartInfo = new ()
                        {
                            FileName = xPdfImagesExePath,
                            // -list would output too much redundant info
                            Arguments = $"-j \"{xPdfFilePath}\" \"{xDestSubdirectoryPath}\\temp\"",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        }
                    };

                    xProcess.OutputDataReceived += (sender, e) =>
                    {
                        if (string.IsNullOrEmpty (e.Data) == false)
                            WriteLineToConsole (e.Data);
                    };

                    xProcess.ErrorDataReceived += (sender, e) =>
                    {
                        if (string.IsNullOrEmpty (e.Data) == false)
                            WriteLineToConsole (e.Data, ConsoleColor.Red);
                    };

                    xProcess.Start ();

                    xProcess.BeginOutputReadLine ();
                    xProcess.BeginErrorReadLine ();

                    xProcess.WaitForExit ();

                    LogsWriter.Flush ();

                    string xSmallDirectoryPath = Path.Join (xDestSubdirectoryPath, "Small"),
                        xGrayscaleDirectoryPath = Path.Join (xDestSubdirectoryPath, "Grayscale");

                    foreach (string xImageFilePath in Directory.GetFiles (xDestSubdirectoryPath, "*.*", SearchOption.TopDirectoryOnly))
                    {
                        string xNewImageFileName = Path.GetFileName (xImageFilePath).Replace ("temp-", string.Empty);
                        string? xNewImageFilePath = null;

                        try
                        {
                            using MagickImage xImage = new (xImageFilePath);

                            if (xImage.Width < 250 && xImage.Height < 250) // Mostly page components
                            {
                                Directory.CreateDirectory (xSmallDirectoryPath);
                                xNewImageFilePath = Path.Join (xSmallDirectoryPath, xNewImageFileName);
                                File.Move (xImageFilePath, xNewImageFilePath);
                                continue;
                            }

                            bool IsColorful (MagickImage image)
                            {
                                if (image.ColorType == ColorType.Bilevel ||
                                        image.ColorType == ColorType.Grayscale ||
                                        image.ColorType == ColorType.GrayscaleAlpha)
                                    return false;

                                if (image.ChannelCount < 3)
                                    return false;

                                var xPixels = image.GetPixels ();

                                int xCheckedPixelCount = 0,
                                    xColorfulPixelCount = 0;

                                for (int x = 0; x < image.Width; x ++)
                                {
                                    for (int y = 0; y < image.Height; y ++)
                                    {
                                        var xPixel = xPixels.GetPixel (x, y);
                                        var xColor = xPixel.ToColor ();

                                        xCheckedPixelCount ++;

                                        if (xColor!.R != xColor!.G || xColor!.G != xColor!.B)
                                            xColorfulPixelCount ++;

                                        // When 100 pixels have been checked, if roughly 3% are colorful
                                        if (xCheckedPixelCount >= 100 && xColorfulPixelCount * 33 >= xCheckedPixelCount)
                                            return true;
                                    }
                                }

                                return false;
                            }

                            if (IsColorful (xImage) == false)
                            {
                                Directory.CreateDirectory (xGrayscaleDirectoryPath);
                                xNewImageFilePath = Path.Join (xGrayscaleDirectoryPath, xNewImageFileName);
                            }

                            else xNewImageFilePath = Path.Join (xDestSubdirectoryPath, xNewImageFileName);

                            File.Move (xImageFilePath, xNewImageFilePath);
                        }

                        catch (Exception xException)
                        {
                            WriteLineToConsole (xException.ToString (), ConsoleColor.Red);
                        }
                    }

                    LogsWriter.Flush ();
                }
            }

            foreach (string xParameter in Parameters)
            {
                if (xParameter.StartsWith ("source_directory_path:"))
                {
                    if (xSourceDirectoryPath != null)
                    {
                        _extractImages (xSourceDirectoryPath, xDestDirectoryPath, xExcludedFileNames);
                        xDestDirectoryPath = null;
                        xExcludedFileNames.Clear ();
                    }

                    xSourceDirectoryPath = xParameter.Substring (xParameter.IndexOf (':') + 1).Trim ();
                }

                else if (xParameter.StartsWith ("dest_directory_path:"))
                    xDestDirectoryPath = xParameter.Substring (xParameter.IndexOf (':') + 1).Trim ();

                else if (xParameter.StartsWith ("excluded_file_name:"))
                    xExcludedFileNames.Add (xParameter.Substring (xParameter.IndexOf (':') + 1).Trim ());
            }

            if (xSourceDirectoryPath != null)
                _extractImages (xSourceDirectoryPath, xDestDirectoryPath, xExcludedFileNames);
        }

        catch (Exception xException)
        {
            WriteLineToConsole (xException.ToString (), ConsoleColor.Red);
        }

        finally
        {
            // For us to know the program hasnt crashed
            LogsWriter.WriteLine ("End of log.");

            Console.Write ("Press any key to exit: ");
            Console.ReadKey (true);
            Console.WriteLine ();

            if (_logsWriter.IsValueCreated) // Just formality; destruction of a lazy-loaded object
                _logsWriter.Value.Dispose ();
        }
    }
}
