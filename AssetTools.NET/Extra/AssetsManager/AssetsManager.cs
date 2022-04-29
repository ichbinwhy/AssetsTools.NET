﻿using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AssetsTools.NET.Extra
{
    public class AssetsManager
    {
        public bool updateAfterLoad = true;
        public bool useTemplateFieldCache = false;
        public ClassDatabasePackage classPackage;
        public ClassDatabaseFile classFile;
        public List<AssetsFileInstance> files = new List<AssetsFileInstance>();
        public List<BundleFileInstance> bundles = new List<BundleFileInstance>();
        private Dictionary<int, AssetTypeTemplateField> templateFieldCache = new Dictionary<int, AssetTypeTemplateField>();
        private Dictionary<string, AssetTypeTemplateField> monoTemplateFieldCache = new Dictionary<string, AssetTypeTemplateField>();

        #region assets files
        public AssetsFileInstance LoadAssetsFile(Stream stream, string path, bool loadDeps, string root = "", BundleFileInstance bunInst = null)
        {
            AssetsFileInstance instance;
            int index = files.FindIndex(f => f.path.ToLower() == Path.GetFullPath(path).ToLower());
            if (index == -1)
            {
                instance = new AssetsFileInstance(stream, path, root);
                instance.parentBundle = bunInst;
                files.Add(instance);
            }
            else
            {
                instance = files[index];
            }

            if (loadDeps)
            {
                if (bunInst == null)
                    LoadDependencies(instance, Path.GetDirectoryName(path));
                else
                    LoadBundleDependencies(instance, bunInst, Path.GetDirectoryName(path));
            }
            if (updateAfterLoad)
                UpdateDependencies(instance);
            return instance;
        }
        public AssetsFileInstance LoadAssetsFile(FileStream stream, bool loadDeps, string root = "")
        {
            return LoadAssetsFile(stream, stream.Name, loadDeps, root);
        }

        public AssetsFileInstance LoadAssetsFile(string path, bool loadDeps, string root = "")
        {
            return LoadAssetsFile(File.OpenRead(path), loadDeps, root);
        }

        public bool UnloadAssetsFile(string path)
        {
            int index = files.FindIndex(f => f.path.ToLower() == Path.GetFullPath(path).ToLower());
            if (index != -1)
            {
                AssetsFileInstance assetsInst = files[index];
                assetsInst.file.Close();
                files.Remove(assetsInst);
                return true;
            }
            return false;
        }

        public bool UnloadAllAssetsFiles(bool clearCache = false)
        {
            if (clearCache)
            {
                templateFieldCache.Clear();
                monoTemplateFieldCache.Clear();
            }

            if (files.Count != 0)
            {
                foreach (AssetsFileInstance assetsInst in files)
                {
                    assetsInst.file.Close();
                }
                files.Clear();
                return true;
            }
            return false;
        }

        public void UnloadAll(bool unloadClassData = false)
        {
            UnloadAllAssetsFiles(true);
            UnloadAllBundleFiles();
            if (unloadClassData)
            {
                classPackage = null;
                classFile = null;
            }
        }
        #endregion

        #region bundle files
        public BundleFileInstance LoadBundleFile(Stream stream, string path, bool unpackIfPacked = true)
        {
            BundleFileInstance bunInst;
            int index = bundles.FindIndex(f => f.path.ToLower() == path.ToLower());
            if (index == -1)
            {
                bunInst = new BundleFileInstance(stream, path, "", unpackIfPacked);
                bundles.Add(bunInst);
            }
            else
            {
                bunInst = bundles[index];
            }
            return bunInst;
        }
        public BundleFileInstance LoadBundleFile(FileStream stream, bool unpackIfPacked = true)
        {
            return LoadBundleFile(stream, Path.GetFullPath(stream.Name), unpackIfPacked);
        }

        public BundleFileInstance LoadBundleFile(string path, bool unpackIfPacked = true)
        {
            return LoadBundleFile(File.OpenRead(path), unpackIfPacked);
        }

        public bool UnloadBundleFile(string path)
        {
            int index = bundles.FindIndex(f => f.path.ToLower() == Path.GetFullPath(path).ToLower());
            if (index != -1)
            {
                BundleFileInstance bunInst = bundles[index];
                bunInst.file.Close();

                foreach (AssetsFileInstance assetsInst in bunInst.loadedAssetsFiles)
                {
                    assetsInst.file.Close();
                }

                bundles.Remove(bunInst);
                return true;
            }
            return false;
        }

        public bool UnloadAllBundleFiles()
        {
            if (bundles.Count != 0)
            {
                foreach (BundleFileInstance bunInst in bundles)
                {
                    bunInst.file.Close();

                    foreach (AssetsFileInstance assetsInst in bunInst.loadedAssetsFiles)
                    {
                        assetsInst.file.Close();
                    }
                }
                bundles.Clear();
                return true;
            }
            return false;
        }

        public AssetsFileInstance LoadAssetsFileFromBundle(BundleFileInstance bunInst, int index, bool loadDeps = false)
        {
            string assetMemPath = Path.Combine(bunInst.path, bunInst.file.GetFileName(index));

            int listIndex = files.FindIndex(f => f.path.ToLower() == Path.GetFullPath(assetMemPath).ToLower());
            if (listIndex == -1)
            {
                if (bunInst.file.IsAssetsFile(index))
                {
                    bunInst.file.GetFileRange(index, out long offset, out long length);
                    SegmentStream stream = new SegmentStream(bunInst.BundleStream, offset, length);
                    AssetsFileInstance assetsInst = LoadAssetsFile(stream, assetMemPath, loadDeps, bunInst: bunInst);
                    bunInst.loadedAssetsFiles.Add(assetsInst);
                    return assetsInst;
                }
            }
            else
            {
                return files[listIndex];
            }
            return null;
        }

        public AssetsFileInstance LoadAssetsFileFromBundle(BundleFileInstance bunInst, string name, bool loadDeps = false)
        {
            int index = bunInst.file.GetFileIndex(name);
            if (index < 0)
                return null;

            return LoadAssetsFileFromBundle(bunInst, index, loadDeps);
        }
        #endregion

        #region dependencies
        public void UpdateDependencies(AssetsFileInstance ofFile)
        {
            var depList = ofFile.file.Metadata.Externals;
            for (int i = 0; i < depList.Count; i++)
            {
                AssetsFileExternal dep = depList[i];
                int index = files.FindIndex(f => Path.GetFileName(dep.PathName.ToLower()) == Path.GetFileName(f.path.ToLower()));
                if (index != -1)
                {
                    ofFile.dependencies[i] = files[index];
                }
            }
        }
        public void UpdateDependencies()
        {
            foreach (AssetsFileInstance file in files)
            {
                UpdateDependencies(file);
            }
        }

        public void LoadDependencies(AssetsFileInstance ofFile, string path)
        {
            for (int i = 0; i < ofFile.dependencies.Count; i++)
            {
                string depPath = ofFile.file.Metadata.Externals[i].PathName;

                if (depPath == string.Empty)
                {
                    continue;
                }

                if (files.FindIndex(f => Path.GetFileName(f.path).ToLower() == Path.GetFileName(depPath).ToLower()) == -1)
                {
                    string absPath = Path.Combine(path, depPath);
                    string localAbsPath = Path.Combine(path, Path.GetFileName(depPath));
                    if (File.Exists(absPath))
                    {
                        LoadAssetsFile(File.OpenRead(absPath), true);
                    }
                    else if (File.Exists(localAbsPath))
                    {
                        LoadAssetsFile(File.OpenRead(localAbsPath), true);
                    }
                }
            }
        }

        public void LoadBundleDependencies(AssetsFileInstance ofFile, BundleFileInstance ofBundle, string path)
        {
            for (int i = 0; i < ofFile.dependencies.Count; i++)
            {
                string depPath = ofFile.file.Metadata.Externals[i].PathName;
                if (files.FindIndex(f => Path.GetFileName(f.path).ToLower() == Path.GetFileName(depPath).ToLower()) == -1)
                {
                    string bunPath = Path.GetFileName(depPath);
                    int bunIndex = Array.FindIndex(ofBundle.file.BlockAndDirInfo.DirectoryInfos, d => Path.GetFileName(d.Name) == bunPath);

                    // by default, the directory of an assets file is the bundle's file path (somepath\bundle.unity3d\file.assets)
                    // we back out again to get the directory the bundle is in
                    string noBunPath = Path.Combine(path, "..");
                    string nbAbsPath = Path.Combine(noBunPath, depPath);
                    string nbLocalAbsPath = Path.Combine(noBunPath, Path.GetFileName(depPath));

                    // if the user chose to set the path to the directory the bundle is in,
                    // we need to check for that as well
                    string absPath = Path.Combine(path, depPath);
                    string localAbsPath = Path.Combine(path, Path.GetFileName(depPath));

                    if (bunIndex != -1)
                    {
                        LoadAssetsFileFromBundle(ofBundle, bunIndex, true);
                    }
                    else if (File.Exists(absPath))
                    {
                        LoadAssetsFile(File.OpenRead(absPath), true);
                    }
                    else if (File.Exists(localAbsPath))
                    {
                        LoadAssetsFile(File.OpenRead(localAbsPath), true);
                    }
                    else if (File.Exists(nbAbsPath))
                    {
                        LoadAssetsFile(File.OpenRead(nbAbsPath), true);
                    }
                    else if (File.Exists(nbLocalAbsPath))
                    {
                        LoadAssetsFile(File.OpenRead(nbLocalAbsPath), true);
                    }
                }
            }
        }
        #endregion

        #region asset resolving
        public AssetExternal GetExtAsset(AssetsFileInstance relativeTo, int fileId, long pathId, bool onlyGetInfo = false, bool forceFromCldb = false)
        {
            AssetExternal ext = new AssetExternal
            {
                info = null,
                baseField = null,
                file = null
            };

            if (fileId == 0 && pathId == 0)
            {
                return ext;
            }
            else if (fileId != 0)
            {
                AssetsFileInstance dep = relativeTo.GetDependency(this, fileId - 1);

                if (dep == null)
                    return ext;

                ext.file = dep;
                ext.info = dep.file.GetAssetInfo(pathId);

                if (ext.info == null)
                    return ext;

                if (!onlyGetInfo)
                    ext.baseField = GetBaseField(dep.file, ext.info, forceFromCldb);
                else
                    ext.baseField = null;

                return ext;
            }
            else
            {
                ext.file = relativeTo;
                ext.info = relativeTo.file.GetAssetInfo(pathId);

                if (ext.info == null)
                    return ext;

                if (!onlyGetInfo)
                    ext.baseField = GetBaseField(relativeTo.file, ext.info, forceFromCldb);
                else
                    ext.baseField = null;

                return ext;
            }
        }

        public AssetExternal GetExtAsset(AssetsFileInstance relativeTo, AssetTypeValueField atvf, bool onlyGetInfo = false, bool forceFromCldb = false)
        {
            int fileId = atvf["m_FileID"].AsInt;
            long pathId = atvf["m_PathID"].AsLong;
            return GetExtAsset(relativeTo, fileId, pathId, onlyGetInfo, forceFromCldb);
        }

        public AssetTypeValueField GetBaseField(AssetsFileInstance inst, AssetFileInfo info, bool forceFromCldb = false)
        {
            return GetBaseField(inst.file, info, forceFromCldb);
        }

        public AssetTypeValueField GetBaseField(AssetsFile file, AssetFileInfo info, bool forceFromCldb = false)
        {
            AssetTypeTemplateField tempField = GetTemplateBaseField(file, info, forceFromCldb);
            AssetTypeValueField valueField = tempField.MakeValue(file.Reader, info.AbsoluteByteStart);
            return valueField;
        }
        #endregion

        #region deserialization
        public AssetTypeTemplateField GetTemplateBaseField(AssetsFile file, AssetFileInfo info, bool forceFromCldb = false)
        {
            ushort scriptIndex = AssetHelper.GetScriptIndex(file, info);
            int fixedId = AssetHelper.FixAudioID(info.TypeId);

            bool hasTypeTree = file.Metadata.TypeTreeNotStripped;
            AssetTypeTemplateField baseField;
            if (useTemplateFieldCache && templateFieldCache.ContainsKey(fixedId))
            {
                baseField = templateFieldCache[fixedId];
            }
            else
            {
                baseField = new AssetTypeTemplateField();
                if (hasTypeTree && !forceFromCldb)
                {
                    baseField.FromTypeTree(AssetHelper.FindTypeTreeTypeByID(file.Metadata, fixedId, scriptIndex));
                }
                else
                {
                    baseField.FromClassDatabase(classFile, AssetHelper.FindAssetClassByID(classFile, fixedId));
                }

                if (useTemplateFieldCache)
                {
                    templateFieldCache[fixedId] = baseField;
                }
            }

            return baseField;
        }

        public AssetTypeValueField GetMonoBaseFieldCached(AssetsFileInstance inst, AssetFileInfo info, string managedPath)
        {
            AssetsFile file = inst.file;
            ushort scriptIndex = AssetHelper.GetScriptIndex(file, info);
            if (scriptIndex == 0xFFFF)
                return null;

            string scriptName;
            if (!inst.monoIdToName.ContainsKey(scriptIndex))
            {
                AssetTypeValueField m_Script = GetBaseField(inst.file, info)["m_Script"];
                
                // no script field
                if (m_Script.IsDummy)
                    return null;

                AssetTypeValueField baseField = GetExtAsset(inst, m_Script).baseField;

                // couldn't find asset
                if (baseField == null)
                    return null;

                scriptName = baseField["m_Name"].AsString;
                string scriptNamespace = baseField["m_Namespace"].AsString;
                string assemblyName = baseField["m_AssemblyName"].AsString;

                if (scriptNamespace == string.Empty)
                {
                    scriptNamespace = "-";
                }

                scriptName = $"{assemblyName}.{scriptNamespace}.{scriptName}";
                inst.monoIdToName[scriptIndex] = scriptName;
            }
            else
            {
                scriptName = inst.monoIdToName[scriptIndex];
            }

            if (monoTemplateFieldCache.ContainsKey(scriptName))
            {
                AssetTypeTemplateField baseTemplateField = monoTemplateFieldCache[scriptName];
                AssetTypeValueField baseField = baseTemplateField.MakeValue(file.Reader, info.AbsoluteByteStart);
                return baseField;
            }
            else
            {
                AssetTypeValueField baseValueField = MonoDeserializer.GetMonoBaseField(this, inst, info, managedPath);
                monoTemplateFieldCache[scriptName] = baseValueField.TemplateField;
                return baseValueField;
            }
        }
        #endregion

        #region class database
        public ClassDatabaseFile LoadClassDatabase(Stream stream)
        {
            classFile = new ClassDatabaseFile();
            classFile.Read(new AssetsFileReader(stream));
            return classFile;
        }

        public ClassDatabaseFile LoadClassDatabase(string path)
        {
            return LoadClassDatabase(File.OpenRead(path));
        }

        public ClassDatabaseFile LoadClassDatabaseFromPackage(string version, bool specific = false)
        {
            if (classPackage == null)
                throw new Exception("No class package loaded!");

            if (specific)
            {
                if (!version.StartsWith("U"))
                    version = "U" + version;
                int index = classPackage.header.files.FindIndex(f => f.name == version);
                if (index == -1)
                    return null;

                classFile = classPackage.files[index];
                return classFile;
            }
            else
            {
                List<ClassDatabaseFile> matchingFiles = new List<ClassDatabaseFile>();
                List<UnityVersion> matchingVersions = new List<UnityVersion>();

                if (version.StartsWith("U"))
                    version = version.Substring(1);

                UnityVersion versionParsed = new UnityVersion(version);

                for (int i = 0; i < classPackage.files.Count; i++)
                {
                    ClassDatabaseFile file = classPackage.files[i];
                    for (int j = 0; j < file.header.unityVersions.Length; j++)
                    {
                        string unityVersion = file.header.unityVersions[j];
                        if (version == unityVersion)
                        {
                            classFile = file;
                            return classFile;
                        }
                        else if (WildcardMatches(version, unityVersion))
                        {
                            string fullUnityVersion = unityVersion;
                            if (fullUnityVersion.EndsWith("*"))
                                fullUnityVersion = file.header.unityVersions[1 - j];

                            matchingFiles.Add(file);
                            matchingVersions.Add(new UnityVersion(fullUnityVersion));
                        }
                    }
                }

                if (matchingFiles.Count == 1)
                {
                    classFile = matchingFiles[0];
                    return classFile;
                }
                else if (matchingFiles.Count > 0)
                {
                    int selectedIndex = 0;
                    int patchNumToMatch = versionParsed.patch;
                    int highestMatchingPatchNum = matchingVersions[selectedIndex].patch;

                    for (int i = 1; i < selectedIndex; i++)
                    {
                        int thisPatchNum = matchingVersions[selectedIndex].patch;
                        if (thisPatchNum > highestMatchingPatchNum && thisPatchNum <= patchNumToMatch)
                        {
                            selectedIndex = i;
                            highestMatchingPatchNum = thisPatchNum;
                        }
                    }

                    classFile = matchingFiles[selectedIndex];
                    return classFile;
                }

                return null;
            }

        }
        private bool WildcardMatches(string test, string pattern)
        {
            return Regex.IsMatch(test, "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$");
        }

        public ClassDatabasePackage LoadClassPackage(Stream stream)
        {
            classPackage = new ClassDatabasePackage();
            classPackage.Read(new AssetsFileReader(stream));
            return classPackage;
        }
        public ClassDatabasePackage LoadClassPackage(string path)
        {
            return LoadClassPackage(File.OpenRead(path));
        }
        #endregion
    }

    public struct AssetExternal
    {
        public AssetFileInfo info;
        public AssetTypeValueField baseField;
        public AssetsFileInstance file;
    }
}
