using System.Diagnostics;
using System.Reflection;
using System.Text;
using ImageMagick;

namespace _pdfImageExtractor;

class Program
{
    private static string AppDirectoryPath => Path.GetDirectoryName (Assembly.GetExecutingAssembly ().Location)!;

    private static string ParametersFilePath => Path.Join (AppDirectoryPath, "Parameters.txt");

    private static Lazy <IEnumerable <string>> _parameters = new (() =>
    {
        if (File.Exists (ParametersFilePath))
            return File.ReadAllLines (ParametersFilePath, Encoding.UTF8).
                Select (x => x.Trim ()).
                Where (y => string.IsNullOrEmpty (y) == false && y.StartsWith ("//") == false);

        return [];
    });

    private static IEnumerable <string> Parameters => _parameters.Value;

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

                Console.WriteLine ("Parameters file created.");
                return;
            }

            string xPdfImagesExePath = Parameters.Single (x => x.StartsWith ("pdfimages_exe_path:"));
            xPdfImagesExePath = xPdfImagesExePath.Substring (xPdfImagesExePath.IndexOf (':') + 1).Trim ();

            if (File.Exists (xPdfImagesExePath) == false)
            {
                Console.WriteLine ("pdfimages.exe not found.");
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
                    Console.WriteLine ($"Source directory not found: {sourceDirectoryPath}");
                    return;
                }

                if (string.IsNullOrEmpty (destDirectoryPath) || Path.IsPathFullyQualified (destDirectoryPath) == false)
                {
                    Console.WriteLine ($"Invalid dest directory path: {destDirectoryPath}");
                    return;
                }

                if (excludedFileNames.Any (x => string.IsNullOrEmpty (x)))
                {
                    Console.WriteLine ("At least one excluded file name is empty.");
                    return;
                }

                Console.BackgroundColor = ConsoleColor.Blue;
                Console.ForegroundColor = ConsoleColor.White;

                Console.WriteLine ("Source directory: " + sourceDirectoryPath);
                Console.WriteLine ("Dest directory: " + destDirectoryPath);

                if (excludedFileNames.Any ())
                    Console.WriteLine ("Excluded file names: " + string.Join (", ", excludedFileNames));

                Console.ResetColor ();

                if (Directory.Exists (destDirectoryPath) && xReextractImages)
                {
                    try
                    {
                        Directory.Delete (destDirectoryPath, recursive: true);
                    }

                    catch
                    {
                        Console.BackgroundColor = ConsoleColor.Red;
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.WriteLine ($"Failed to delete: {destDirectoryPath}");
                        Console.ResetColor ();

                        return;
                    }
                }

                Directory.CreateDirectory (destDirectoryPath);

                foreach (string xPdfFilePath in Directory.GetFiles (sourceDirectoryPath, "*.pdf", SearchOption.TopDirectoryOnly))
                {
                    string xPdfFileName = Path.GetFileName (xPdfFilePath);

                    if (excludedFileNames.Contains (xPdfFileName, StringComparer.OrdinalIgnoreCase))
                    {
                        Console.WriteLine ($"Images extraction skipped for: {xPdfFileName}");
                        continue;
                    }

                    string xDestSubdirectoryPath = Path.Join (destDirectoryPath, Path.GetFileNameWithoutExtension (xPdfFilePath));

                    if (Directory.Exists (xDestSubdirectoryPath))
                    {
                        Console.WriteLine ($"Images already extracted for: {xPdfFileName}");
                        continue;
                    }

                    Console.WriteLine ($"Extracting images for: {xPdfFileName}");

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
                            Console.WriteLine (e.Data);
                    };

                    xProcess.ErrorDataReceived += (sender, e) =>
                    {
                        if (string.IsNullOrEmpty (e.Data) == false)
                            Console.WriteLine (e.Data);
                    };

                    xProcess.Start ();

                    xProcess.BeginOutputReadLine ();
                    xProcess.BeginErrorReadLine ();

                    xProcess.WaitForExit ();

                    foreach (string xImageFilePath in Directory.GetFiles (xDestSubdirectoryPath, "*.*", SearchOption.TopDirectoryOnly))
                    {
                        string xNewImageFileName = Path.GetFileName (xImageFilePath).Replace ("temp-", string.Empty);
                        string? xNewImageFilePath = null;

                        try
                        {
                            using MagickImage xImage = new (xImageFilePath);

                            if (xImage.Width < 100 && xImage.Height < 100) // Many are page design components
                            {
                                File.Delete (xImageFilePath);
                                continue;
                            }

                            if (xImage.ChannelCount < 3)
                            {
                                Directory.CreateDirectory (Path.Join (Path.GetDirectoryName (xImageFilePath), "Grayscale")); // Lazy coding
                                xNewImageFilePath = Path.Join (Path.GetDirectoryName (xImageFilePath), "Grayscale", xNewImageFileName);
                            }

                            else xNewImageFilePath = Path.Join (Path.GetDirectoryName (xImageFilePath), xNewImageFileName);

                            File.Move (xImageFilePath, xNewImageFilePath);
                        }

                        catch
                        {
                            // We wont touch what we dont understand
                        }
                    }
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
            Console.BackgroundColor = ConsoleColor.Red;
            Console.ForegroundColor = ConsoleColor.White;

            Console.WriteLine (xException.ToString ());

            Console.ResetColor ();
        }

        finally
        {
            Console.Write ("Press any key to exit: ");
            Console.ReadKey (true);
            Console.WriteLine ();
        }
    }
}
