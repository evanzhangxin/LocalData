//----------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//----------------------------------------------------------------

namespace SlnGen
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using Microsoft.Build.BuildEngine;
    using Microsoft.Build.Framework;

    public class MProjToCsProj
    {
        const string BuildSettings = "MICROSOFT.BUILD.SETTINGS";
        const string CSharpTargets = @"$(MSBuildBinPath)\Microsoft.CSharp.targets";
        const string MTargets = "MICROSOFT.M.TARGETS";
        const string MTargetsPath = "$(MTargetsPath)";
        const string MEmbeddedTargets = @"$(MSBuildExtensionsPath32)\Microsoft\M\v1.0\Microsoft.M.Embedded.targets";
        const string OsloCSharpTargets = @"$(PkgMsBuild_Corext_3_5)\Microsoft.Build.CSharp.targets";
        const string OsloMTargets = @"$(INETROOT)\PRIVATE\TOOLS\BUILD\MICROSOFT.OSLO.M.TARGETS";
        const string OsloMEmbeddedTargets = @"$(INETROOT)\PRIVATE\TOOLS\BUILD\Microsoft.Oslo.M.Embedded.targets";

        public static ProjectInfo ConvertIfIsMProj(VSPath vsPath, VSPath csProjPath, Config.Verbosity verbosity)
        {
            if (vsPath.extension.ToLowerInvariant() != ".mproj")
            {
                return new ProjectInfo(vsPath.fullName, verbosity);
            }

            // Make sure current file do not exists
            if (File.Exists(csProjPath.fullName))
            {
                File.Delete(csProjPath.fullName);
            }

            Project csProj = new Project();
            csProj.Load(vsPath.fullName);

            // First, change the import
            bool isOsloProj = false;
            Import importToRemove = null;
            foreach (Import import in csProj.Imports)
            {
                string importedProject = import.ProjectPath.ToUpperInvariant();
                if (!import.IsImported && (
                    importedProject.Contains(MTargets) || importedProject.Contains(OsloMTargets) || import.ProjectPath.Contains(MTargetsPath)))
                {
                    importToRemove = import;
                }
                
                else if (importedProject.Contains(BuildSettings))
                {
                    isOsloProj = true;
                }
            }

            if (importToRemove != null)
            {
                csProj.Imports.RemoveImport(importToRemove);
            }

            if (isOsloProj)
            {
                csProj.AddNewImport(OsloCSharpTargets, "");
                csProj.AddNewImport(OsloMEmbeddedTargets, "");
            }
            else
            {
                csProj.AddNewImport(CSharpTargets, "");
                csProj.AddNewImport(MEmbeddedTargets, "");
            }

            // Second, change the Compile to MCompile item and Reference to MReference
            foreach (BuildItemGroup itemGroup in csProj.ItemGroups)
            {
                if (itemGroup.IsImported)
                {
                    continue;
                }

                foreach (BuildItem item in itemGroup)
                {
                    switch (item.Name)
                    {
                        case "Compile":
                            item.Name = "MCompile";
                            break;
                        case "Reference":
                            item.Name = "MReference";
                            break;
                        case "EmbeddedResource":
                            if (item.Include.EndsWith("preInstall.sql"))
                            {
                                item.Name = "MPreInstallSql";
                            }
                            else if (item.Include.EndsWith("postInstall.sql"))
                            {
                                item.Name = "MPostInstallSql";
                            }
                            else
                            {
                                item.Name = "MResource";
                            }
                            break;
                        case "PreInstallSql":
                            item.Name = "MPreInstallSql";
                            break;
                        case "PostInstallSql":
                            item.Name = "MPostInstallSql";
                            break;
                        case "ProjectReference":
                            item.Include = item.Include.Replace(".mproj", "_generated.csproj");
                            break;
                        default:
                            break;
                    }
                }
            }

            // Third, remove unnecessary Properties and change required properties
            foreach (BuildPropertyGroup propertyGroup in csProj.PropertyGroups)
            {
                if (propertyGroup.IsImported)
                {
                    continue;
                }

                List<BuildProperty> propertiesToAdd = new List<BuildProperty>();
                List<BuildProperty> propertiesToRemove = new List<BuildProperty>();
                
                foreach (BuildProperty property in propertyGroup)
                {
                    BuildProperty newProp = null;
                    BuildProperty oldProp = null;
                    switch (property.Name)
                    {
                        case "MPackageScript":
                            oldProp = property;
                            break;
                        case "MPackageImage":
                            oldProp = property;
                            break;
                        case "MTargetsPath":
                            oldProp = property;
                            break;
                        case "Codepage":
                            newProp = new BuildProperty("MCodepage", property.Value);
                            oldProp = property;
                            break;
                        case "ConflictLogFile":
                            newProp = new BuildProperty("MConflictLog", property.Value);
                            oldProp = property;
                            break;
                       case "DefineConstants":
                            newProp = new BuildProperty("MDefineConstants", property.Value);
                            oldProp = property;
                            break;
                        case "FeatureVersion":
                            newProp = new BuildProperty("MFeatureVersion", property.Value);
                            oldProp = property;
                            break;
                        case "IncludeStandardLibrary":
                            newProp = new BuildProperty("MIncludeStandardLibrary", property.Value);
                            oldProp = property;
                            break;
                        case "NoCatalog":
                            newProp = new BuildProperty("MNoCatalog", property.Value);
                            oldProp = property;
                            break;
                        case "NoDeprecationWarnings":
                            newProp = new BuildProperty("MNoDeprecationWarnings", property.Value);
                            oldProp = property;
                            break;
                        case "NoWarn":
                            newProp = new BuildProperty("MNoWarn", property.Value);
                            oldProp = property;
                            break;
                        case "PrintReport":
                            newProp = new BuildProperty("MPrintReport", property.Value);
                            oldProp = property;
                            break;
                        case "ReportConflicts":
                            newProp = new BuildProperty("MReportConflicts", property.Value);
                            oldProp = property;
                            break;
                        case "SemanticAnalysisThreshold":
                            newProp = new BuildProperty("MSemanticAnalysisThreshold", property.Value);
                            oldProp = property;
                            break;
                        case "ServicingVersion":
                            newProp = new BuildProperty("MServicingVersion", property.Value);
                            oldProp = property;
                            break;
                        case "Verbose":
                            newProp = new BuildProperty("MVerbose", property.Value);
                            oldProp = property;
                            break;
                        case "WarningsAsError":
                            newProp = new BuildProperty("MWarningsAsError", property.Value);
                            oldProp = property;
                            break;
                        case "WarningAsErrors":
                            newProp = new BuildProperty("MWarningAsErrors", property.Value);
                            oldProp = property;
                            break;
                        default:
                            break;
                    }
                    if (newProp != null)
                    {
                        propertiesToAdd.Add(newProp);
                    }
                    if (oldProp != null)
                    {
                        propertiesToRemove.Add(oldProp);
                    }
                }
                foreach (var oldProp in propertiesToRemove)
                {
                    propertyGroup.RemoveProperty(oldProp);
                }
                foreach (var newProp in propertiesToAdd)
                {
                    propertyGroup.AddNewProperty(newProp.Name, newProp.Value);
                }
            }

            // Fourth, add required csproj properties
            string oldAssemblyName = csProj.GetEvaluatedProperty("AssemblyName");
            csProj.SetProperty("AssemblyName", oldAssemblyName + "_generated");
            csProj.SetProperty("OutputType", "Library");
            csProj.SetProperty("MxOutputFileName", oldAssemblyName + ".mx");

            csProj.Save(csProjPath.fullName);
            return new ProjectInfo(csProjPath.fullName, verbosity);
        }
    }
}
