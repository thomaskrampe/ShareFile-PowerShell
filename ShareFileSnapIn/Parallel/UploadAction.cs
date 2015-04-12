﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ShareFile.Api.Client.Exceptions;
using ShareFile.Api.Client.FileSystem;
using ShareFile.Api.Client.Transfers;
using ShareFile.Api.Client.Transfers.Downloaders;
using ShareFile.Api.Models;

namespace ShareFile.Api.Powershell.Parallel
{
    /// <summary>
    /// UploadAction class to upload file to ShareFile server
    /// </summary>
    class UploadAction : IAction
    {
        private FileSystemInfo child;
        private Client.ShareFileClient client;
        private Models.Item uploadTarget;
        private String details;
        private FileSupport fileSupportDelegate;
        private ActionType actionType;

        public UploadAction(FileSupport fileSupport, Client.ShareFileClient client, FileSystemInfo source, Models.Item target, String details, ActionType type)
        {
            this.client = client;
            this.child = source;
            this.uploadTarget = target;
            this.details = details;
            this.fileSupportDelegate = fileSupport;
            actionType = type;
        }

        void IAction.CopyFileItem(ProgressInfo progressInfo)
        {
            var fileInfo = (FileInfo)child;
            Models.Item fileItem = null;
            try
            {
                fileItem = client.Items.ByPath(uploadTarget.url, "/" + child.Name).Execute();
            }
            catch (ODataException e) {
                if (e.Code != System.Net.HttpStatusCode.NotFound)
                {
                    throw e;
                }
            }

            bool duplicate = fileItem != null && fileItem is Models.File;
            bool hashcodeMatches = duplicate ? (fileItem as Models.File).Hash.Equals(Utility.GetMD5HashFromFile(child.FullName)) : false;

            if(duplicate && actionType == ActionType.None) 
            {
                throw new IOException("File already exist");
            } 
            else if (!duplicate || actionType == ActionType.Force || (actionType == ActionType.Sync && !hashcodeMatches ))
            {
                var uploadSpec = new UploadSpecificationRequest
                {
                    CanResume = false,
                    Details = details,
                    FileName = fileInfo.Name,
                    FileSize = fileInfo.Length,
                    Method = UploadMethod.Threaded,
                    Parent = uploadTarget.url,
                    ThreadCount = 4,
                    Raw = true
                };

                var uploader = client.GetAsyncFileUploader(uploadSpec, new PlatformFileInfo(fileInfo));

                progressInfo.ProgressTotal(progressInfo.FileIndex, fileInfo.Length);

                uploader.OnTransferProgress =
                    (sender, args) =>
                    {
                        if (args.Progress.TotalBytes > 0)
                        {
                            progressInfo.ProgressTransferred(progressInfo.FileIndex, args.Progress.BytesTransferred);
                        }
                        
                    };
                
                Task.Run(() => uploader.UploadAsync()).Wait();
                fileSupportDelegate(fileInfo.Name);
            }
        }

    }
}
