using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows;
using System.Xml.Linq;

namespace IntunePackagingTool
{
    public static class IntuneWinDebugger
    {
        public static void InspectIntuneWinFile(string intuneWinPath)
        {
            var report = $"=== INSPECTING: {Path.GetFileName(intuneWinPath)} ===\n\n";
            
            try
            {
                using (var archive = ZipFile.OpenRead(intuneWinPath))
                {
                    report += "FILES INSIDE .INTUNEWIN:\n";
                    foreach (var entry in archive.Entries)
                    {
                        report += $"  - {entry.Name} ({entry.Length:N0} bytes)\n";
                    }
                    
                    report += "\n";
                    
                    // Look for detection.xml
                    var detectionEntry = archive.Entries.FirstOrDefault(e => 
                        e.Name.Equals("detection.xml", StringComparison.OrdinalIgnoreCase));
                    
                    if (detectionEntry != null)
                    {
                        report += "✓ FOUND detection.xml\n\n";
                        
                        using (var stream = detectionEntry.Open())
                        using (var reader = new StreamReader(stream))
                        {
                            var xmlContent = reader.ReadToEnd();
                            report += "DETECTION.XML CONTENT:\n";
                            report += "==========================================\n";
                            report += xmlContent;
                            report += "\n==========================================\n\n";
                            
                            // Try to parse and show structure
                            try
                            {
                                var doc = XDocument.Parse(xmlContent);
                                report += "XML STRUCTURE ANALYSIS:\n";
                                report += $"Root element: {doc.Root?.Name}\n";
                                
                                if (doc.Root != null)
                                {
                                    report += "Child elements:\n";
                                    foreach (var element in doc.Root.Elements())
                                    {
                                        report += $"  - {element.Name}\n";
                                        
                                        // Show nested elements for key nodes
                                        if (element.Name.LocalName == "EncryptionInfo")
                                        {
                                            foreach (var child in element.Elements())
                                            {
                                                report += $"    - {child.Name}: {child.Value[..Math.Min(20, child.Value.Length)]}...\n";
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception xmlEx)
                            {
                                report += $"❌ XML PARSING ERROR: {xmlEx.Message}\n";
                            }
                        }
                    }
                    else
                    {
                        report += "❌ detection.xml NOT FOUND!\n";
                        
                        // Look for similar files
                        var xmlFiles = archive.Entries.Where(e => e.Name.EndsWith(".xml")).ToList();
                        if (xmlFiles.Any())
                        {
                            report += "Found these XML files instead:\n";
                            foreach (var xmlFile in xmlFiles)
                            {
                                report += $"  - {xmlFile.Name}\n";
                            }
                        }
                    }
                    
                    // Look for .dat files
                    var datFiles = archive.Entries.Where(e => e.Name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)).ToList();
                    report += $"\n💾 DAT FILES ({datFiles.Count}):\n";
                    foreach (var file in datFiles)
                    {
                        report += $"  - {file.Name} ({file.Length:N0} bytes)\n";
                    }
                    
                    if (datFiles.Count == 0)
                    {
                        report += "\n❌ NO .DAT FILES FOUND!\n";
                        report += "Possible content files (largest files):\n";
                        
                        var largestFiles = archive.Entries
                            .Where(e => !e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                            .OrderByDescending(e => e.Length)
                            .Take(3)
                            .ToList();
                        
                        foreach (var file in largestFiles)
                        {
                            report += $"  - {file.Name} ({file.Length:N0} bytes)\n";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                report += $"❌ ERROR INSPECTING FILE: {ex.Message}\n";
            }
            
            // Show the report
            MessageBox.Show(report, "IntuneWin File Inspection", MessageBoxButton.OK, MessageBoxImage.Information);
            
            // Also write to debug console
            System.Diagnostics.Debug.WriteLine(report);
        }
    }
}