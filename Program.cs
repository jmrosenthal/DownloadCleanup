using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace DownloadCleanup
{
    class Program
    {
        static void Main(string[] args)
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string downloadsPath = Path.Combine(userProfile, "Downloads");
            string archivePath = Path.Combine(downloadsPath, "Archiv");

            if (!Directory.Exists(downloadsPath))
            {
                Console.WriteLine("Downloads-Ordner nicht gefunden.");
                return;
            }

            if (!Directory.Exists(archivePath))
            {
                Directory.CreateDirectory(archivePath);
            }

            DateTime currentDate = DateTime.Now;

            // 1. Zuerst Duplikate identifizieren (sucht in Downloads UND im Archiv!)
            Console.WriteLine("Suche nach Duplikaten...");
            RemoveDuplicates(downloadsPath, archivePath);

            // 2. Dateien verschieben (älter als 30 Tage)
            Console.WriteLine("Verschiebe alte Dateien...");
            ProcessFiles(downloadsPath, archivePath, currentDate);

            // 3. Vergangene Jahre automatisch archivieren
            Console.WriteLine("Prüfe auf zu archivierende vergangene Jahre...");
            ArchivePastYears(archivePath, currentDate.Year);
            
            Console.WriteLine("\nAufräumen abgeschlossen.");
            //Console.WriteLine("Drücke ENTER, um das Fenster zu schließen...");
            //Console.ReadLine(); // Hält das Fenster offen, damit du Fehler/Erfolge lesen kannst!
        }

        static void RemoveDuplicates(string downloadsPath, string archivePath)
        {
            List<FileInfo> allFiles = new List<FileInfo>();

            // Dateien aus dem Haupt-Download-Ordner holen
            DirectoryInfo dlDir = new DirectoryInfo(downloadsPath);
            if (dlDir.Exists)
            {
                allFiles.AddRange(dlDir.GetFiles());
            }

            // Dateien aus dem Archiv holen (inklusive aller Unterordner wie 2023-11 etc.)
            DirectoryInfo archDir = new DirectoryInfo(archivePath);
            if (archDir.Exists)
            {
                allFiles.AddRange(archDir.GetFiles("*.*", SearchOption.AllDirectories));
            }

            // 1. Nach exakter Dateigröße (in Bytes) gruppieren
            var sizeGroups = allFiles.GroupBy(f => f.Length).Where(g => g.Count() > 1);

            foreach (var sizeGroup in sizeGroups)
            {
                // 2. Hashwert (Inhalt) vergleichen
                var hashGroups = sizeGroup.GroupBy(f => GetFileHash(f.FullName));

                foreach (var hashGroup in hashGroups)
                {
                    if (hashGroup.Key != null && hashGroup.Count() > 1)
                    {
                        // 3. Schlau sortieren: 
                        // Zuerst Dateien behalten, die schon im Archiv liegen.
                        // Bei Gleichstand die älteste Datei behalten.
                        var sortedFiles = hashGroup
                            .OrderByDescending(f => f.DirectoryName.StartsWith(archivePath, StringComparison.OrdinalIgnoreCase))
                            .ThenBy(f => f.CreationTime)
                            .ToList();

                        // Das Original (Index 0) behalten, alle anderen (Index 1 bis n) löschen
                        for (int i = 1; i < sortedFiles.Count; i++)
                        {
                            try
                            {
                                sortedFiles[i].Delete();
                                Console.WriteLine($"[GELÖSCHT] Duplikat: {sortedFiles[i].Name} (aus {sortedFiles[i].DirectoryName})");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[FEHLER] Kann {sortedFiles[i].Name} nicht löschen: {ex.Message}");
                            }
                        }
                    }
                }
            }
        }

        static string? GetFileHash(string filePath)
        {
            try
            {
                using (var md5 = MD5.Create())
                {
                    using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        var hash = md5.ComputeHash(stream);
                        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ÜBERSPRUNGEN] Konnte Hash für {Path.GetFileName(filePath)} nicht lesen: {ex.Message}");
                return null; 
            }
        }

        static void ProcessFiles(string sourcePath, string archivePath, DateTime currentDate)
        {
            string[] files = Directory.GetFiles(sourcePath);

            foreach (string file in files)
            {
                FileInfo fileInfo = new FileInfo(file);
                
                if (fileInfo.LastWriteTime < currentDate.AddMonths(-1))
                {
                    string monthFolderName = fileInfo.LastWriteTime.ToString("yyyy-MM");
                    string targetDirectory = Path.Combine(archivePath, monthFolderName);

                    if (!Directory.Exists(targetDirectory))
                    {
                        Directory.CreateDirectory(targetDirectory);
                    }

                    string targetFilePath = Path.Combine(targetDirectory, fileInfo.Name);

                    try
                    {
                        if (!File.Exists(targetFilePath))
                        {
                            File.Move(file, targetFilePath);
                            Console.WriteLine($"[VERSCHOBEN] {fileInfo.Name} -> {monthFolderName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[FEHLER] Beim Verschieben von {fileInfo.Name}: {ex.Message}");
                    }
                }
            }
        }

        static void ArchivePastYears(string archivePath, int currentYear)
        {
            string[] directories = Directory.GetDirectories(archivePath);

            foreach (string dir in directories)
            {
                DirectoryInfo dirInfo = new DirectoryInfo(dir);

                if (Regex.IsMatch(dirInfo.Name, @"^\d{4}-\d{2}$"))
                {
                    if (int.TryParse(dirInfo.Name.Substring(0, 4), out int folderYear))
                    {
                        if (folderYear < currentYear)
                        {
                            string yearArchivePath = Path.Combine(archivePath, folderYear.ToString());
                            
                            if (!Directory.Exists(yearArchivePath))
                            {
                                Directory.CreateDirectory(yearArchivePath);
                            }

                            string targetDir = Path.Combine(yearArchivePath, dirInfo.Name);

                            try
                            {
                                Directory.Move(dir, targetDir);
                                Console.WriteLine($"[ARCHIVIERT] Monatsordner {dirInfo.Name} -> {folderYear}");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[FEHLER] Beim Archivieren von {dirInfo.Name}: {ex.Message}");
                            }
                        }
                    }
                }
            }
        }
    }
}
