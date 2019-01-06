﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Xml;

namespace PackageReferenceEditor
{
    public static class Updater
    {
        private static void Save(PackageReference v)
        {
            v.Document.Save(v.FileName);
        }

        public static string NormalizePath(string path)
        {
            return path.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        }

        public static void FindReferences(string searchPath, string searchPattern, IEnumerable<string> ignoredPaths, IList<PackageReference> references, IList<XmlDocument> documents)
        {
            Directory.EnumerateFiles(searchPath, searchPattern, SearchOption.AllDirectories).ToList().ForEach(
                fileName =>
                {
                    if (!ignoredPaths.Any(i => NormalizePath(fileName).Contains(NormalizePath(i))))
                    {
                        FindReferences(fileName, references, documents);
                    }
                });
        }

        public static void FindReferences(string fileName, IList<PackageReference> references, IList<XmlDocument> documents)
        {
            var document = new XmlDocument() { PreserveWhitespace = true };
            document.Load(fileName);
            documents.Add(document);

            var root = document.DocumentElement;
            var nodes = root.SelectNodes("descendant::PackageReference");

            foreach (XmlNode node in nodes)
            {
                var name = node.Attributes["Include"];
                var versionAttribute = node.Attributes["Version"];
                var version = default(string);
                if (versionAttribute != null)
                {
                    version = versionAttribute.Value;
                }
                else
                {
                    var versions = node.SelectNodes("descendant::Version");
                    if (versions.Count > 0)
                    {
                        version = versions[0].Value;
                    }
                }
                var pr = new PackageReference()
                {
                    Name = name.Value,
                    Version = version,
                    FileName = fileName,
                    Document = document,
                    Reference = node,
                    VersionAttribute = versionAttribute
                };
                references.Add(pr);
            }
        }

        public static UpdaterResult FindReferences(this UpdaterResult updater, string searchPath, string searchPattern, IEnumerable<string> ignoredPaths)
        {
            FindReferences(searchPath, searchPattern, ignoredPaths, updater.References, updater.Documents);

            updater.GroupedReferences = updater.References
                .GroupBy(x => x.Name)
                .OrderBy(x => x.Key)
                .ToDictionary(x => x.Key, x => (IList<PackageReference>)new ObservableCollection<PackageReference>(x));

            return updater;
        }

        public static UpdaterResult FindReferences(string searchPath, string searchPattern, IEnumerable<string> ignoredPaths)
        {
            return new UpdaterResult()
            {
                Documents = new ObservableCollection<XmlDocument>(),
                References = new ObservableCollection<PackageReference>()
            }
            .FindReferences(searchPath, searchPattern, ignoredPaths);
        }

        public static void PrintVersions(this UpdaterResult result)
        {
            Logger.Log("NuGet package dependencies versions:");
            foreach (var package in result.GroupedReferences)
            {
                Logger.Log($"Package {package.Key} is installed:");
                foreach (var v in package.Value)
                {
                    Logger.Log($"{v.Version}, {v.FileName}");
                }
            }
        }

        public static void ValidateVersions(this UpdaterResult result)
        {
            Logger.Log("Checking installed NuGet package dependencies versions:");
            foreach (var package in result.GroupedReferences)
            {
                var packageVersion = package.Value.First().Version;
                bool isValidVersion = package.Value.All(x => x.Version == packageVersion);
                if (!isValidVersion)
                {
                    Logger.Log($"Error: package {package.Key} has multiple versions installed:");
                    foreach (var v in package.Value)
                    {
                        Logger.Log($"{v.Version}, {v.FileName}");
                    }
                    throw new Exception("Detected multiple NuGet package version installed for different projects.");
                }
            };
            Logger.Log("All NuGet package dependencies versions are valid.");
        }

        public static void UpdateVersions(this UpdaterResult result, string name, string version)
        {
            Logger.Log("Updating NuGet package dependencies versions:");
            foreach (var v in result.GroupedReferences[name])
            {
                if (v.VersionAttribute != null)
                {
                    if (version != v.VersionAttribute.Value)
                    {
                        Logger.Log($"Name: {name}, old: {v.VersionAttribute.Value}, new: {version}, file: {v.FileName}");
                        v.VersionAttribute.Value = version;
                        Save(v);
                    }
                }
                else
                {
                    var versions = v.Reference.SelectNodes("descendant::Version");
                    if (versions.Count > 0)
                    {
                        var versionElement = versions[0];
                        if (version != versionElement.Value)
                        {
                            Logger.Log($"Name: {name}, old: {versionElement.Value}, new: {version}, file: {v.FileName}");
                            versionElement.Value = version;
                            Save(v);
                        }
                    }
                }
            }
        }

        public static void UpdateVersions(KeyValuePair<string, IList<PackageReference>> package, bool alwaysUpdate = false)
        {
            Logger.Log($"Updating NuGet package dependencies versions for {package.Key}:");
            foreach (var v in package.Value)
            {
                if (v.VersionAttribute != null)
                {
                    if (v.Version != v.VersionAttribute.Value || alwaysUpdate == true)
                    {
                        Logger.Log($"Name: {package.Key}, old: {v.VersionAttribute.Value}, new: {v.Version}, file: {v.FileName}");
                        v.VersionAttribute.Value = v.Version;
                        Save(v);
                    }
                }
                else
                {
                    var versions = v.Reference.SelectNodes("descendant::Version");
                    if (versions.Count > 0)
                    {
                        var versionElement = versions[0];
                        if (v.Version != versionElement.Value || alwaysUpdate == true)
                        {
                            Logger.Log($"Name: {package.Key}, old: {versionElement.Value}, new: {v.Version}, file: {v.FileName}");
                            versionElement.Value = v.Version;
                            Save(v);
                        }
                    }
                }
            }
        }
    }
}
