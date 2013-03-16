﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using AshMind.Extensions;
using CopyAndReplace.UI;
using EnvDTE;
using Microsoft.VisualStudio.Project;
using Microsoft.VisualStudio.Shell;

namespace CopyAndReplace.Implementation {
    public class Controller {
        private readonly IDebugLogger logger;

        private bool inPaste = false;
        private readonly IList<ProjectItem> pastedItems = new List<ProjectItem>();
        private IDictionary<FileInfo, string> filesInClipboardWithContent; 

        public Controller(IDebugLogger logger) {
            this.logger = logger;
        }

        public void HandleBeforePaste() {
            this.inPaste = true;
            this.pastedItems.Clear();
            this.filesInClipboardWithContent = GetFilesInClipboard().ToDictionary(path => new FileInfo(path), File.ReadAllText);
            this.logger.WriteLine("Before paste: {0} items in clipboard:", this.filesInClipboardWithContent.Count);
            foreach (var file in this.filesInClipboardWithContent.Keys) {
                this.logger.WriteLine("  " + file.FullName);
            }
        }

        public void HandleAddedItem(ProjectItem item) {
            if (!this.inPaste)
                return;

            this.logger.WriteLine("Pasted: " + item.Name);
            this.pastedItems.Add(item);
        }
        
        public void HandleAfterPaste() {
            this.logger.WriteLine("After paste: {0} items to process:", this.pastedItems.Count);
            this.inPaste = false;

            if (this.pastedItems.Count == 0) {
                this.logger.WriteLine("  Nothing to process.");
                return;
            }

            var filesBeforeCopy = this.pastedItems.ToDictionary(item => item, this.GetFileBeforeCopy);
            var viewModel = this.GetReplacementViewModel(this.pastedItems[0], filesBeforeCopy[this.pastedItems[0]]);
            if (viewModel == null) {
                this.logger.WriteLine("  Processing cancelled by user.");
                return;
            }

            var replacer = new StringCaseAwareReplacer(viewModel.Pattern, viewModel.Replacement);
            foreach (var item in this.pastedItems) {
                this.RenameAndReplace(item, filesBeforeCopy[item], replacer);                
            }
        }

        private IEnumerable<string> GetFilesInClipboard() {
            if (Clipboard.ContainsFileDropList())
                return Clipboard.GetFileDropList().Cast<string>();

            var oleDataObject = new OleDataObject(System.Windows.Forms.Clipboard.GetDataObject());
            DropDataType dropDataType;
            var stgItems = DragDropHelper.GetDroppedFiles(DragDropHelper.CF_VSSTGPROJECTITEMS, oleDataObject, out dropDataType);
            if (stgItems.Count > 0) {
                return stgItems.Select(item => item.Split('|'))
                               .Select(parts => CorrectPathCapitalization(parts[2]));
            }

            return Enumerable.Empty<string>();
        }

        private string CorrectPathCapitalization(string path) {
            var parent = Path.GetDirectoryName(path);
            if (parent == null)
                return path.ToUpperInvariant(); // root, drive

            var corrected = Path.GetFileName(Directory.GetFileSystemEntries(parent, Path.GetFileName(path)).Single());
            return Path.Combine(this.CorrectPathCapitalization(parent), corrected);
        }

        private ReplacementViewModel GetReplacementViewModel(ProjectItem item, FileInfo fileBeforeCopy) {
            var initialText = Path.GetFileNameWithoutExtension(fileBeforeCopy != null ? fileBeforeCopy.Name : item.Name);

            var dialog = new ReplaceDialog {
                ViewModel = new ReplacementViewModel {
                    Pattern = initialText,
                    Replacement = initialText
                }
            };
            var shouldReplace = dialog.ShowModal() ?? false;
            if (!shouldReplace)
                return null;

            return dialog.ViewModel;
        }

        private FileInfo GetFileBeforeCopy(ProjectItem item) {
            var itemContent = File.ReadAllText(GetFullPath(item));

            return (
                from candidate in filesInClipboardWithContent
                where item.Name.EndsWith(candidate.Key.Name, StringComparison.InvariantCultureIgnoreCase)
                where candidate.Value == itemContent
                select candidate.Key
            ).FirstOrDefault();
        }
        
        private string GetFullPath(ProjectItem item) {
            return (string)item.Properties.Item("FullPath").Value;
        }

        private void RenameAndReplace(ProjectItem item, FileInfo fileBeforeCopy, StringCaseAwareReplacer replacer) {
            var loggedReplacements = new HashSet<string>();

            this.logger.WriteLine("  {0}:", item.Name);
            this.logger.WriteLine("    Copied from: {0}", fileBeforeCopy != null ? fileBeforeCopy.FullName : "<unknown>");
            var replacementUsedHandler = (EventHandler<ReplacementUsedEventArgs>)((sender, e) => {
                if (loggedReplacements.Contains(e.Match))
                    return;

                this.logger.WriteLine("    Replacing: \"{0}\" -> \"{1}\"", e.Match, e.Replacement);
                loggedReplacements.Add(e.Match);
            });

            replacer.ReplacementUsed += replacementUsedHandler;

            var canonicalName = fileBeforeCopy != null ? fileBeforeCopy.Name : item.Name;
            var renamed = replacer.ReplaceAllIn(canonicalName);

            if (renamed != canonicalName)
                item.Name = renamed;

            var fullPath = GetFullPath(item);
            var content = File.ReadAllText(this.GetFullPath(item));
            File.WriteAllText(fullPath, replacer.ReplaceAllIn(content));

            replacer.ReplacementUsed -= replacementUsedHandler;
        }
    }
}
