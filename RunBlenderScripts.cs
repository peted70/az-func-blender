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
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", "get", Route = null)] HttpRequest req,
            ILogger log)
        {
            PostData data = new PostData();

            if (req.Method == "GET")
            {
                var inputZip = req.Query["InputZipUri"];
                if (!string.IsNullOrEmpty(inputZip))
                {
                    data.InputZipUri = new Uri(inputZip);
                    var format = req.Query["OutputFormat"];
                    if (!string.IsNullOrEmpty(format))
                        data.OutputFormat = format;
                }
            }
            else if (req.Method == "POST")
            {
                var content = await new StreamReader(req.Body).ReadToEndAsync();
                data = JsonConvert.DeserializeObject<PostData>(content);
            }

            var workingDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var root = Path.GetPathRoot(workingDir);

            log.LogInformation($"working dir = {workingDir}");

            var http = new HttpClient();
            log.LogInformation($"HTTP GET = {data.InputZipUri.ToString()}");

            HttpResponseMessage response = null;
            try
            {
                response = await http.GetAsync(data.InputZipUri);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Http failure");
            }

            var za = new ZipArchive(await response.Content.ReadAsStreamAsync(), ZipArchiveMode.Read);
            var zipDir = root + "tmp/objzip/" + Guid.NewGuid();

            log.LogInformation($"zip dir = {zipDir}");

            za.ExtractToDirectory(zipDir, true);

            // find the obj file in the root..
            DirectoryInfo DirectoryInWhichToSearch = new DirectoryInfo(zipDir);
            FileInfo objFile = DirectoryInWhichToSearch.GetFiles("*.obj").Single();

            // objFilePath parameter
            var ObjFilePathParameter = objFile.FullName;
            var OutputFormatParameter = data.OutputFormat;

            log.LogInformation("C# HTTP trigger function processed a request.");
            log.LogInformation("We have life!");

            log.LogInformation(Directory.GetCurrentDirectory());

            if (File.Exists("/usr/bin/blender-2.83.4-linux64/blender"))
                log.LogInformation("found exe");

            ProcessStartInfo processInfo = null;
            try
            {
                var commandArguments = "-b -P /local/scripts/objmat.py -- -i " + ObjFilePathParameter + " -o " + OutputFormatParameter;
                log.LogInformation($"commandArguments = {commandArguments}");
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

            var outputDir = Path.Combine(zipDir, "converted");
            var exists = Directory.Exists(outputDir) ? "yes" : "no";
            log.LogInformation($"Does output directory exist? {exists}");

            // If we get this far we might have some binary output so write it to a zip archive and retun
            //
            using (var fs = new FileStream(Path.Combine(zipDir, "Output.zip"), FileMode.OpenOrCreate))
            using (var OutputZip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                OutputZip.CreateEntryFromDirectory(outputDir);
                log.LogInformation("Created output zip archive");

                return new FileStreamResult(fs, new MediaTypeHeaderValue("application/zip"))
                {
                    FileDownloadName = "output.zip"
                };
            }
        }
    }
}
