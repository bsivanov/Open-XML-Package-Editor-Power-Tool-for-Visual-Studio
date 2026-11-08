/****************************************************************************

    DocumentPackage.cs

    Wrapper class for a Package, its storage, and its meta data.

------------------------------------------------------------- LICENSE BEGINS HERE--------------------------------------------------------------------------------------

Copyright (c) Microsoft Corporation
All rights reserved. 

Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0 

THIS CODE IS PROVIDED ON AN  *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY IMPLIED WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A PARTICULAR PURPOSE, MERCHANTABLITY OR NON-INFRINGEMENT. 

See the Apache Version 2.0 License for specific language governing permissions and limitations under the License.
-------------------------------------------------------------- LICENSE ENDS HERE -----------------------------------------------------------------------------------------
****************************************************************************/
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.IO.Packaging;
using System.ComponentModel;
using System.Diagnostics;

namespace Microsoft.OpenXMLEditor
{
    // [CLSCompliant(false)]
    public class DocumentPackage
    {
        private string untitledName;
        private string filePath;
        private MemoryStream memStream;
        private Package package;
        private bool dirty;

        private Dictionary<string, DocumentPart> docParts;

        public void Close()
        {
            if (package == null)
                return;
            docParts.Clear();
            package.Close();
            package = null;
            memStream.Close();
            memStream = null;
            filePath = null;
            dirty = false;
        }

        public bool Open(string path)
        {
            try
            {
                FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read);
                byte[] buffer = new byte[fs.Length];
                fs.Read(buffer, 0, (int)fs.Length);
                memStream = new MemoryStream();
                memStream.Write(buffer, 0, (int)fs.Length);
                buffer = null;
                fs.Close();

                package = Package.Open(memStream, FileMode.Open, FileAccess.ReadWrite);

                docParts = new Dictionary<string, DocumentPart>();
                PackagePartCollection parts = package.GetParts();
                foreach (PackagePart part in parts)
                {
                    DocumentPart docPart = new DocumentPart(part);
                    docParts.Add(part.Uri.ToString(), docPart);
                }

                filePath = path;
                dirty = false;
                                
                return true;
            }
            catch (Exception e)
            {
                System.Windows.Forms.MessageBox.Show(e.Message);
                return false;
            }
        }

        public bool Save()
        {
            if (string.IsNullOrEmpty(filePath) || IsUntitled)
                return false;
            return SaveAs(filePath);
        }

        public bool SaveAs(string path)
        {
            if (SaveCopyAs(path))
            {
                filePath = path;
                untitledName = null;
                return true;
            }
            return false;
        }

        public bool SaveCopyAs(string path)
        {
            string tempPath = null;
            try
            {
                ActivityLogger.Log(string.Format(
                    "SaveCopyAs begin path='{0}' targetExisted={1}",
                    path, File.Exists(path)));

                // Serialize the document by copying all parts and relationships
                // through the READ path into a brand-new package written to a
                // temp file. Writing the live package's own stream relies on
                // Package.Flush(), which under the Visual Studio 2026 packaging
                // stack (System.IO.Packaging 11.0.0.0 from Common7\IDE\AppLocalWPF)
                // serializes the ORIGINAL entry data (GitHub issue #10). Creating
                // a new package uses a write path that works on every stack, and
                // the live in-memory package is left untouched, so open part
                // windows, the tree, and history stay valid.
                tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (Package newPackage = Package.Open(tempPath, FileMode.Create))
                {
                    // Package-level relationships.
                    foreach (PackageRelationship rel in package.GetRelationships())
                    {
                        newPackage.CreateRelationship(rel.TargetUri, rel.TargetMode, rel.RelationshipType, rel.Id);
                    }

                    // Parts, preserving content type and compression. The read
                    // path reflects every edit made through DocumentPart.Text.
                    // Relationship parts are NOT copied as parts: the packaging
                    // regenerates them from the explicit CreateRelationship
                    // calls below (copying them would double-register their
                    // relationships and fail with ID conflicts).
                    foreach (PackagePart part in package.GetParts())
                    {
                        if (part.ContentType.EndsWith("relationships+xml"))
                        {
                            continue;
                        }
                        PackagePart newPart = newPackage.CreatePart(part.Uri, part.ContentType, part.CompressionOption);
                        using (Stream inStream = part.GetStream())
                        using (Stream outStream = newPart.GetStream())
                        {
                            inStream.CopyTo(outStream);
                        }
                    }

                    // Per-part relationships. Relationship parts cannot have
                    // relationships of their own, so skip them.
                    foreach (PackagePart part in package.GetParts())
                    {
                        if (part.ContentType.EndsWith("relationships+xml"))
                        {
                            continue;
                        }
                        PackagePart newPart = newPackage.GetPart(part.Uri);
                        foreach (PackageRelationship rel in part.GetRelationships())
                        {
                            newPart.CreateRelationship(rel.TargetUri, rel.TargetMode, rel.RelationshipType, rel.Id);
                        }
                    }
                }

                ActivityLogger.Log(string.Format(
                    "SaveCopyAs serialized tempLen={0}",
                    new FileInfo(tempPath).Length));

                // Swap the temp file into place, so a failed write never
                // truncates the original file.
                if (File.Exists(path))
                {
                    FileAttributes attributes = File.GetAttributes(path);
                    File.SetAttributes(tempPath, attributes);
                    File.Replace(tempPath, path, null);
                }
                else
                {
                    File.Move(tempPath, path);
                }
                tempPath = null;

                ActivityLogger.Log(string.Format(
                    "SaveCopyAs verify-after-write fileLen={0} fileHash={1}",
                    new FileInfo(path).Length, HashHex(File.ReadAllBytes(path))));

                dirty = false;
                return true;
            }
            catch (Exception e)
            {
                try
                {
                    if (tempPath != null && File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                }

                ActivityLogger.Log("SaveCopyAs ERROR: " + e.Message);
                System.Windows.Forms.MessageBox.Show(e.Message);
                return false;
            }
        }

        /// <summary>
        /// Diagnostic: first 8 hex chars of the MD5 hash of a byte array.
        /// Used only for save-path logging.
        /// </summary>
        private static string HashHex(byte[] bytes)
        {
            using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
            {
                return BitConverter.ToString(md5.ComputeHash(bytes)).Replace("-", "").Substring(0, 8);
            }
        }

        public void ReloadFromMemory()
        {
            Debug.Assert(memStream != null);

            this.docParts = null;
            package.Close();
            package = null;
            package = Package.Open(memStream, FileMode.Open, FileAccess.ReadWrite);

            docParts = new Dictionary<string, DocumentPart>();
            PackagePartCollection parts = package.GetParts();
            foreach (PackagePart part in parts)
            {
                DocumentPart docPart = new DocumentPart(part);
                docParts.Add(part.Uri.ToString(), docPart);
            }
        }

        [Browsable(false)]
        public string UntitledName
        {
            get
            {
                return untitledName;
            }
            set
            {
                untitledName = value;
            }
        }

        [Browsable(false)]
        public bool IsUntitled
        {
            get
            {
                return !string.IsNullOrEmpty(untitledName);
            }
        }

        [Category("Misc")]
        [DisplayName("Full Path")]
        [Description("Location of the file.")]
        public string _FilePath
        {
            get
            {
                if (IsUntitled)
                    return null;
                return filePath;
            }
        }

        [Browsable(false)]
        public string FilePath
        {
            get
            {
                return filePath;
            }
        }

        [Category("Misc")]
        [DisplayName("File Name")]
        [Description("Name of the file.")]
        public string FileName
        {
            get
            {
                if (IsUntitled)
                    return untitledName;
                return System.IO.Path.GetFileName(filePath);
            }
        }

        [Browsable(false)]
        public Dictionary<string, DocumentPart> DocParts
        {
            get
            {
                return docParts;
            }
        }

        [Browsable(false)]
        public Package Package
        {
            get
            {
                return package;
            }
        }

        [Browsable(false)]
        public bool IsDirty
        {
            get
            {
                return dirty;
            }
            set
            {
                dirty = value;
            }
        }

        [Category("Document Properties")]
        [DisplayName("Category")]
        [Description("Category of the document.")]
        public string Category
        {
            get
            {
                return package.PackageProperties.Category;
            }
        }

        [Category("Document Properties")]
        [DisplayName("Content Status")]
        [Description("Content status of the document.")]
        public string ContentStatus
        {
            get
            {
                return package.PackageProperties.ContentStatus;
            }
        }

        [Category("Document Properties")]
        [DisplayName("Content Type")]
        [Description("Content type of the document.")]
        public string ContentType
        {
            get
            {
                return package.PackageProperties.ContentType;
            }
        }

        [Category("Document Properties")]
        [DisplayName("Created")]
        [Description("Date and time the document was created.")]
        public DateTime? Created
        {
            get
            {
                return package.PackageProperties.Created;
            }
        }

        [Category("Document Properties")]
        [DisplayName("Creator")]
        [Description("Creator of the document.")]
        public string Creator
        {
            get
            {
                return package.PackageProperties.Creator;
            }
        }

        [Category("Document Properties")]
        [DisplayName("Description")]
        [Description("Description of the document.")]
        public string Description
        {
            get
            {
                return package.PackageProperties.Description;
            }
        }

        [Category("Document Properties")]
        [DisplayName("Identifier")]
        [Description("Identifier of the document.")]
        public string Identifier
        {
            get
            {
                return package.PackageProperties.Identifier;
            }
        }

        [Category("Document Properties")]
        [DisplayName("Keywords")]
        [Description("Keywords of the document.")]
        public string Keywords
        {
            get
            {
                return package.PackageProperties.Keywords;
            }
        }

        [Category("Document Properties")]
        [DisplayName("Language")]
        [Description("Language of the document.")]
        public string Language
        {
            get
            {
                return package.PackageProperties.Language;
            }
        }

        [Category("Document Properties")]
        [DisplayName("Last Modified By")]
        [Description("Author of the last modification to the document.")]
        public string LastModifiedBy
        {
            get
            {
                return package.PackageProperties.LastModifiedBy;
            }
        }

        [Category("Document Properties")]
        [DisplayName("Last Printed")]
        [Description("Date and time the document was last printed.")]
        public DateTime? LastPrinted
        {
            get
            {
                return package.PackageProperties.LastPrinted;
            }
        }

        [Category("Document Properties")]
        [DisplayName("Modified")]
        [Description("Date and time the document was last modified.")]
        public DateTime? Modified
        {
            get
            {
                return package.PackageProperties.Modified;
            }
        }

        [Category("Document Properties")]
        [DisplayName("Revision")]
        [Description("Revision number of the document.")]
        public string Revision
        {
            get
            {
                return package.PackageProperties.Revision;
            }
        }

        [Category("Document Properties")]
        [DisplayName("Subject")]
        [Description("Subject of the document.")]
        public string Subject
        {
            get
            {
                return package.PackageProperties.Subject;
            }
        }

        [Category("Document Properties")]
        [DisplayName("Title")]
        [Description("Title of the document.")]
        public string Title
        {
            get
            {
                return package.PackageProperties.Title;
            }
        }

        [Category("Document Properties")]
        [DisplayName("Version")]
        [Description("Version of the document.")]
        public string Version
        {
            get
            {
                return package.PackageProperties.Version;
            }
        }
    }
}
