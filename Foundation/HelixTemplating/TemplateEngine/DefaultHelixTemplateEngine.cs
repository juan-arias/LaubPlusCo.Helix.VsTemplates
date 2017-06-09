﻿/* Sitecore Helix Visual Studio Templates 
 * 
 * Copyright (C) 2017, Anders Laub - Laub plus Co, DK 29 89 76 54 contact@laubplusco.net https://laubplusco.net
 * 
 * Permission to use, copy, modify, and/or distribute this software for any purpose with or without fee is hereby granted, 
 * provided that the above copyright notice and this permission notice appear in all copies.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH REGARD TO THIS SOFTWARE INCLUDING 
 * ALL IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, 
 * DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, 
 * WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE 
 * OR PERFORMANCE OF THIS SOFTWARE.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LaubPlusCo.Foundation.HelixTemplating.Data;
using LaubPlusCo.Foundation.HelixTemplating.Manifest;
using LaubPlusCo.Foundation.HelixTemplating.Services;

namespace LaubPlusCo.Foundation.HelixTemplating.TemplateEngine
{
  public class DefaultHelixTemplateEngine : IHelixTemplateEngine
  {
    protected virtual HelixTemplateManifest Manifest { get; set; }
    protected virtual ReplaceTokensService ReplaceTokensService { get; set; }
    protected virtual BuildDestinationPathService BuildDestinationPathService { get; set; }
    protected virtual string DestinationRootPath { get; set; }

    public IHelixProjectTemplate Run(HelixTemplateManifest manifest, string solutionRootPath, IDictionary<string, string> replacementTokens)
    {
      Manifest = manifest;
      ReplaceTokensService = new ReplaceTokensService(replacementTokens);
      DestinationRootPath = solutionRootPath;
      BuildDestinationPathService = new BuildDestinationPathService(Manifest.ManifestRootPath, DestinationRootPath);
      var templateObjects = new List<ITemplateObject>();
      templateObjects.AddRange(GetTemplateObjectFromDirectory(Manifest.ManifestRootPath));
      var copyTemplateObjectsService = new CopyTemplateObjectFilesService(templateObjects);
      var copiedFilePaths = copyTemplateObjectsService.Copy();

      if (!copiedFilePaths.Any())
        return new HelixProjectTemplate
        {
          Manifest = Manifest,
          TemplateObjects = templateObjects,
          ReplacementTokens = replacementTokens
        };

      var replaceFileTokensService = new ReplaceTokensInFilesService(copiedFilePaths, replacementTokens);
      replaceFileTokensService.Replace();
      MarkProjectContent(templateObjects);
      CreateVirtualSolutionFolders(templateObjects);
      return new HelixProjectTemplate
      {
        Manifest = Manifest,
        TemplateObjects = templateObjects,
        ReplacementTokens = replacementTokens
      };
    }

    protected virtual void CreateVirtualSolutionFolders(IList<ITemplateObject> templateObjects)
    {
      if (Manifest.VirtualSolutionFolders == null || !Manifest.VirtualSolutionFolders.Any())
        return;
      var sourceRootObject = FindSourceRootTemplateObjectService.Find(templateObjects);
      GetVirtualSolutionFolderTemplateObjects(sourceRootObject, Manifest.VirtualSolutionFolders, Path.Combine(sourceRootObject.DestinationFullPath));
    }

    protected virtual void GetVirtualSolutionFolderTemplateObjects(ITemplateObject root, IList<VirtualSolutionFolder> virtualSolutionFolders, string parentPath)
    {
      foreach (var virtualSolutionFolder in virtualSolutionFolders)
      {
        var virtualFolderObject = new TemplateObject
        {
          Type = TemplateObjectType.Folder,
          DestinationFullPath = Path.Combine(parentPath, virtualSolutionFolder.Name)
        };

        foreach (var filePath in virtualSolutionFolder.Files)
        {
          virtualFolderObject.ChildObjects.Add(new TemplateObject
          {
            ChildObjects = null,
            Type = TemplateObjectType.File,
            OriginalFullPath = filePath,
            DestinationFullPath = ReplaceTokensService.Replace(BuildDestinationPathService.Build(filePath))
          });
        }

        if (virtualSolutionFolder.SubFolders == null || !virtualSolutionFolders.Any())
          continue;
        GetVirtualSolutionFolderTemplateObjects(virtualFolderObject, virtualSolutionFolder.SubFolders, virtualFolderObject.DestinationFullPath);
        root.ChildObjects.Add(virtualFolderObject);
      }
    }

    protected virtual void MarkProjectContent(IList<ITemplateObject> templateObjects)
    {
      foreach (var templateObject in templateObjects)
      {
        if (templateObject.Type == TemplateObjectType.Project)
          IsProjectContent(templateObjects.Where(to => to.Type != TemplateObjectType.Project));
        if (templateObject.ChildObjects == null || !templateObject.ChildObjects.Any())
          continue;
        MarkProjectContent(templateObject.ChildObjects);
      }
    }

    protected virtual void IsProjectContent(IEnumerable<ITemplateObject> projectContentTemplateObjects)
    {
      foreach (var projectContentTemplateObject in projectContentTemplateObjects)
      {
        projectContentTemplateObject.IsProjectContent = true;
        if (projectContentTemplateObject.ChildObjects == null || !projectContentTemplateObject.ChildObjects.Any())
          continue;
        IsProjectContent(projectContentTemplateObject.ChildObjects);
      }
    }

    protected virtual IList<ITemplateObject> GetTemplateObjectFromDirectory(string directoryPath)
    {
      var templateObjects = Directory.EnumerateFiles(directoryPath).Select(GetTemplateObjectFromFile).Where(objectFromFile => objectFromFile != null).ToList();
      templateObjects.AddRange(Directory.EnumerateDirectories(directoryPath)
        .Select(directory => new TemplateObject
        {
          Type = IsSourceRoot(directory) ? TemplateObjectType.SourceRoot : TemplateObjectType.Folder,
          ChildObjects = GetTemplateObjectFromDirectory(directory),
          OriginalFullPath = directory,
          DestinationFullPath = ReplaceTokensService.Replace(BuildDestinationPathService.Build(directory))
        }));
      return templateObjects;
    }

    protected virtual ITemplateObject GetTemplateObjectFromFile(string filePath)
    {
      var isIgnored = IsIgnored(filePath);
      return new TemplateObject
      {
        Type = IsProjectToAttach(filePath) ? TemplateObjectType.Project : TemplateObjectType.File,
        ChildObjects = null,
        OriginalFullPath = filePath,
        IsIgnored = isIgnored,
        DestinationFullPath = isIgnored ? "" : ReplaceTokensService.Replace(BuildDestinationPathService.Build(filePath))
      };
    }

    protected virtual bool IsIgnored(string filePath)
    {
      return Manifest.IgnoreFiles.Any(skipFilePath => skipFilePath.Equals(filePath, StringComparison.InvariantCultureIgnoreCase));
    }

    protected virtual bool IsSourceRoot(string path)
    {
      return Manifest.SourceFolder.Equals(path, StringComparison.InvariantCultureIgnoreCase);
    }

    protected virtual bool IsProjectToAttach(string filePath)
    {
      return Manifest.ProjectsToAttach.Any(projectPath => projectPath.Equals(filePath, StringComparison.InvariantCultureIgnoreCase));
    }
  }
}