using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Net.Http;
using System.IO.Compression;
using Microsoft.Net.Http.Headers;
using System.Reflection;

namespace AzFuncDocker
{
    public static class ZipArchiveExtension 
    {
        public static void CreateEntryFromAny(this ZipArchive archive, string sourceName, string entryName = "") 
        {
            var fileName = Path.GetFileName(sourceName);
            if (File.GetAttributes(sourceName).HasFlag(FileAttributes.Directory)) 
            {
                archive.CreateEntryFromDirectory(sourceName, Path.Combine(entryName, fileName));
            } 
            else 
            {
                archive.CreateEntryFromFile(sourceName, Path.Combine(entryName, fileName), CompressionLevel.Fastest);
            }
        }

        public static void CreateEntryFromDirectory(this ZipArchive archive, string sourceDirName, string entryName = "") 
        {
            string[] files = Directory.GetFiles(sourceDirName).Concat(Directory.GetDirectories(sourceDirName)).ToArray();
            archive.CreateEntry(Path.Combine(entryName, Path.GetFileName(sourceDirName)));
            foreach (var file in files) 
            {
                archive.CreateEntryFromAny(file, entryName);
            }
        }
    }

    internal class PostData
    {
        public Uri InputZipUri { get; set; }
        public string OutputFormat { get; set; }
    }

    public static class RunBlenderScripts
    {
        // Pass in an input parameter url to a zip file and an output parameter specifying the output format 
        // Example command line to build:
        // blender -b -P objmat.py -- -i "C:\Users\peted\3D Objects\Gold.obj" -o gltf
        //
        [FunctionName("RunBlenderScripts")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            var workingDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var root = Path.GetPathRoot(workingDir);

            log.LogInformation($"working dir = {workingDir}");

            var content = await new StreamReader(req.Body).ReadToEndAsync();
            var postData = JsonConvert.DeserializeObject<PostData>(content);

            var http = new HttpClient();
            var response = await http.GetAsync(postData.InputZipUri);

            var za = new ZipArchive(await response.Content.ReadAsStreamAsync(), ZipArchiveMode.Read);
            var zipDir = root + "tmp/objzip/" + Guid.NewGuid();
            za.ExtractToDirectory(zipDir, true);

            // find the obj file in the root..
            DirectoryInfo DirectoryInWhichToSearch = new DirectoryInfo(zipDir);
            FileInfo objFile = DirectoryInWhichToSearch.GetFiles("*.obj").Single();

            // objFilePath parameter
            var ObjFilePathParameter = objFile.FullName;
            var OutputFormatParameter = postData.OutputFormat;

            log.LogInformation("C# HTTP trigger function processed a request.");
            log.LogInformation("We have life!");

            log.LogInformation(Directory.GetCurrentDirectory());

            if (File.Exists("/usr/bin/blender-2.83.4-linux64/blender"))
                log.LogInformation("found exe");

            ProcessStartInfo processInfo = null;
            try
            {
                var commandArguments = "-b -P /local/scripts/objmat.py -- -i " + ObjFilePathParameter + " -o " + OutputFormatParameter;
                processInfo = new ProcessStartInfo("/usr/bin/blender-2.83.4-linux64/blender", commandArguments);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "ProcessStartInfo: " + ex.Message);
            }

            log.LogInformation(string.Join(" ", DirectoryInWhichToSearch.GetFiles().Select(fi => fi.FullName)));

            processInfo.CreateNoWindow = true;
            processInfo.UseShellExecute = false;

            processInfo.RedirectStandardError = true;
            processInfo.RedirectStandardOutput = true;

            Process process = null;
            try
            {
                process = Process.Start(processInfo);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Process.Start: " + ex.Message);
            }

            string output = string.Empty;
            string err = string.Empty;

            if (process != null)
            { 
                // Read the output (or the error)
                output = process.StandardOutput.ReadToEnd();
                err = process.StandardError.ReadToEnd();
                process.WaitForExit();
            }

            string responseMessage = "Standard Output: " + output;
            responseMessage += "\n\nStandard Error: " + err;

            // If we get this far we might have some binary output so write it to a zip archive and retun
            //
            using (var fs = new FileStream(Path.Combine(zipDir, "Output.zip"), FileMode.OpenOrCreate))
            using (var OutputZip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                OutputZip.CreateEntryFromDirectory(zipDir);

                return new FileStreamResult(fs, new MediaTypeHeaderValue("text/plain"))
                {
                    FileDownloadName = "output.zip"
                };
            }
        }
    }
}
