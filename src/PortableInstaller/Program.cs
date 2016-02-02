﻿using SyncTrayzor.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PortableInstaller
{
    class Program
    {
        static int Main(string[] args)
        {
            RecycleBinDeleter.Logger = s => Console.WriteLine("!! " + s);

            if (args.Length != 4)
            {
                Console.WriteLine("You should not invoke this executable directly. It is used as part of the automatic upgrade process for portable installations.");
                Console.ReadKey();
                return 0;
            }
            try
            {
                var destinationPath = args[0];
                var sourcePath = args[1];
                var waitForPid = Int32.Parse(args[2]);
                var pathToRestartApplication = args[3];

                bool pauseAtEnd = false;

                Console.WriteLine("Waiting for SyncTrayzor process to exit...");
                try
                {
                    using (var process = Process.GetProcessById(waitForPid))
                    {
                        if (!process.WaitForExit(5000))
                            throw new Exception($"SyncTrayzor process with PID {waitForPid} did not exit after 5 seconds");
                    }
                }
                catch (ArgumentException) // It wasn't running to start with. Coolio
                { }

                // By default our CWD is the destinationPath, which locks it
                var cwd = Path.GetDirectoryName(destinationPath);
                try
                {
                    Directory.SetCurrentDirectory(cwd);
                }
                catch (Exception)
                {
                    Console.WriteLine($"!! Unable to set working directory to\n    {cwd}.\nNone of your files have been touched.");
                    throw;
                }

                var destinationExists = Directory.Exists(destinationPath);

                if (!Directory.Exists(sourcePath))
                {
                    Console.WriteLine($"!! Unable to find source path\n    {sourcePath}.\nThis is a bug with SyncTrayzor's upgrade mechanism.");
                    throw new Exception("Unable to find source path");
                }

                string movedDestinationPath = null;
                if (destinationExists)
                {
                    movedDestinationPath = GenerateBackupDestinationPath(destinationPath);
                    while (true)
                    {
                        Console.WriteLine($"Moving\n    {destinationPath}\nto\n    {movedDestinationPath}");
                        try
                        {
                            DirectoryMove(destinationPath, movedDestinationPath);
                            break;
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine();
                            Console.WriteLine($"!! Unable to move\n    {destinationPath}\nto\n    {movedDestinationPath}");
                            Console.WriteLine($"Error: {e.GetType().Name} {e.Message}");
                            Console.WriteLine($"!! Please make sure that\n    {destinationPath}\nor any of the files inside it, aren't open.");
                            Console.WriteLine($"!! Press any key to try again, or Ctrl-C to abort the upgrade.");
                            Console.WriteLine($"!! If you abort the upgrade, none of your files will be modified.");
                            Console.ReadKey();
                        }
                    }
                }

                Console.WriteLine($"Moving\n    {sourcePath}\nto\n    {destinationPath}");
                try
                {
                    DirectoryMove(sourcePath, destinationPath);
                }
                catch (Exception)
                {
                    Console.WriteLine();
                    Console.WriteLine($"!! Unable to move\n    {sourcePath}\nto\n    {destinationPath}.\nYour copy of SyncTrayzor is at\n    {sourcePath}\nand will still work.");
                    throw;
                }

                if (destinationExists)
                {
                    var sourceDataFolder = Path.Combine(movedDestinationPath, "data");
                    var destDataFolder = Path.Combine(destinationPath, "data");
                    if (Directory.Exists(sourceDataFolder))
                    {
                        Console.WriteLine();
                        Console.WriteLine($"Copying data folder\n    {sourceDataFolder}\nto\n    {destDataFolder}...");
                        try
                        {
                            DirectoryCopy(sourceDataFolder, destDataFolder);
                        }
                        catch (Exception)
                        {
                            Console.WriteLine();
                            Console.WriteLine($"!! Unable to copy\n    {sourceDataFolder}\nto\n    {destDataFolder}.\nYour copy of SyncTrayzor is at\n    {movedDestinationPath}\nand will still work.");
                            throw;
                        }
                    }
                    else
                    {
                        Console.WriteLine();
                        Console.WriteLine($"!! Could not find source data folder {sourceDataFolder}, so not copying. If you have ever started SyncTrayzor from {movedDestinationPath}, this is an error: please manually copy your 'data' folder from whereever it is to {destDataFolder}");
                        pauseAtEnd = true;
                    }

                    var sourceInstallCount = Path.Combine(movedDestinationPath, "InstallCount.txt");
                    var destInstallCount = Path.Combine(destinationPath, "InstallCount.txt");
                    if (File.Exists(sourceInstallCount))
                    {
                        var installCount = Int32.Parse(File.ReadAllText(sourceInstallCount).Trim());
                        Console.WriteLine($"Increasing install count to {installCount + 1} from\n    {sourceInstallCount}\nto\n    {destInstallCount}");
                        try
                        {
                            File.WriteAllText(destInstallCount, (installCount + 1).ToString());
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine();
                            Console.WriteLine($"!! Unable to increase install count: {e.GetType().Name} {e.Message}. Continuing anyway.");
                            pauseAtEnd = true;
                        }
                    }
                    else
                    {
                        Console.WriteLine($"{sourceInstallCount}\ndoesn't exist, so setting installCount to 1 in\n    {destInstallCount}");
                        try
                        {
                            File.WriteAllText(destInstallCount, "1");
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine();
                            Console.WriteLine($"!! Unable to set install count: {e.GetType().Name} {e.Message}. Continuing anyway.");
                            pauseAtEnd = true;
                        }
                    }

                    Console.WriteLine($"Deleting\n    {movedDestinationPath}\nto the recycle bin");
                    try
                    {
                        RecycleBinDeleter.Delete(movedDestinationPath);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine();
                        Console.WriteLine($"!! Unable to delete your old installation at\n    {movedDestinationPath}\n Error: {e.GetType().Name} {e.Message}.");
                        Console.WriteLine($"Your new installation is at\n    {destinationPath}\nand should be fully functional.");
                        Console.WriteLine($"Please double-check, and manually delete\n    {movedDestinationPath}.");
                        pauseAtEnd = true;
                    }
                }

                if (pauseAtEnd)
                {
                    Console.WriteLine();
                    Console.WriteLine();
                    Console.WriteLine("One or more warnings occurred. Please review the messages above, and take any appropriate action.");
                    Console.WriteLine("Press any key to continue (this will restart SyncTrayzor)");
                    Console.ReadKey();
                }

                Console.WriteLine($"Restarting application {pathToRestartApplication}");
                Process.Start(pathToRestartApplication);

                return 0;
            }
            catch (Exception e)
            {
                Console.WriteLine();
                Console.WriteLine($"--- An error occurred ---");
                Console.WriteLine($"{e.GetType().Name}: {e.Message}");
                Console.WriteLine();
                Console.WriteLine("The upgrade failed to complete successfully. Sorry about that.");
                Console.WriteLine("Please read the messages above.");
                Console.WriteLine("Press any key to continue");
                Console.ReadKey();
                return 2;
            }
        }

        private static string GenerateBackupDestinationPath(string path)
        {
            for (int i = 1; i < 1000; i++)
            {
                var tempPath = $"{path}_{i}";
                if (!Directory.Exists(tempPath) && !File.Exists(tempPath))
                {
                    return tempPath;
                }
            }

            throw new Exception("Could not generate a backup path");
        }

        // From https://msdn.microsoft.com/en-us/library/bb762914%28v=vs.110%29.aspx
        private static void DirectoryCopy(string sourceDirName, string destDirName)
        {
            // Get the subdirectories for the specified directory.
            DirectoryInfo dir = new DirectoryInfo(sourceDirName);

            if (!dir.Exists)
                throw new DirectoryNotFoundException($"Source directory does not exist or could not be found: {sourceDirName}");

            DirectoryInfo[] dirs = dir.GetDirectories();
            // If the destination directory doesn't exist, create it.
            if (!Directory.Exists(destDirName))
                Directory.CreateDirectory(destDirName);

            // Get the files in the directory and copy them to the new location.
            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string temppath = Path.Combine(destDirName, file.Name);
                file.CopyTo(temppath, true); // Overwrite
            }

            foreach (DirectoryInfo subdir in dirs)
            {
                string temppath = Path.Combine(destDirName, subdir.Name);
                DirectoryCopy(subdir.FullName, temppath);
            }
        }

        private static void DirectoryMove(string sourceDirName, string destDirName)
        {
            // Don't care about things like different names for the same root
            if (Path.GetPathRoot(sourceDirName) == Path.GetPathRoot(destDirName))
            {
                Directory.Move(sourceDirName, destDirName);
            }
            else
            {
                DirectoryCopy(sourceDirName, destDirName);
                Directory.Delete(sourceDirName, true);
            }
        }
    }
}
