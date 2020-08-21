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
using System.Reflection;
using System.Net;

namespace AzFuncDocker
{
    public static class RunBlenderScripts
    {
        // Location of the blender executable in the container
        //
        private const string BlenderExeLocation = @"/usr/bin/blender-2.83.4-linux64/blender";
        private const string ArchiveName = "3DModel.zip";
        private const string TempRootDir = "tmp/objzip/";

        // Pass in an input parameter url to a zip file and an output parameter specifying the output format 
        // Example command line to build:
        // blender -b -P objmat.py -- -i "C:\Users\peted\3D Objects\Gold.obj" -o gltf
        //
        [FunctionName("RunBlenderScripts")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", "get", Route = null)] HttpRequest req,
            ILogger log)
        {
            // Abstract handling the request parameters. I have set this up to use a GET or a POST for 
            // convenience of testing..
            //
            var data = await ProcessParameters(req);

            var workingDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var root = Path.GetPathRoot(workingDir);

            log.LogInformation($"working dir = {workingDir}");

            // Download the zip file containing the payload.
            //
            HttpResponseMessage response = null;
            using (var http = new HttpClient())
            {
                log.LogInformation($"HTTP GET = {data.InputZipUri}");

                try
                {
                    response = await http.GetAsync(data.InputZipUri);
                }
                catch (HttpRequestException hex)
                {
                    log.LogError(hex, $"Request to {data.InputZipUri} Failed");
                }
                catch (Exception ex)
                {
                    log.LogError(ex, $"Request to {data.InputZipUri} Failed");
                }
            }

            // Extract the input zip archive into a temp location
            //
            var zipDir = root + TempRootDir + Guid.NewGuid();
            log.LogInformation($"zip dir = {zipDir}");

            using (var za = new ZipArchive(await response.Content.ReadAsStreamAsync(), ZipArchiveMode.Read))
            {
                za.ExtractToDirectory(zipDir, true);
            }

            // find the obj file in the root of the extracted archive
            //
            DirectoryInfo DirectoryInWhichToSearch = new DirectoryInfo(zipDir);
            FileInfo objFile = DirectoryInWhichToSearch.GetFiles("*.obj").Single();

            // objFilePath parameter
            var ObjFilePathParameter = objFile.FullName;
            var OutputFormatParameter = data.OutputFormat;

            if (!File.Exists(BlenderExeLocation))
            {
                log.LogInformation($"Unable to locate Blender executable {BlenderExeLocation}");
                return new StatusCodeResult((int)HttpStatusCode.InternalServerError);
            }

            ProcessStartInfo processInfo;
            try
            {
                var commandArguments = "-b -P /local/scripts/objmat.py -- -i " + ObjFilePathParameter + " -o " + OutputFormatParameter;
                log.LogInformation($"commandArguments = {commandArguments}");
                processInfo = new ProcessStartInfo(BlenderExeLocation, commandArguments);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "ProcessStartInfo: " + ex.Message);
                return new StatusCodeResult((int)HttpStatusCode.InternalServerError);
            }

            processInfo.CreateNoWindow = true;
            processInfo.UseShellExecute = false;

            processInfo.RedirectStandardError = true;
            processInfo.RedirectStandardOutput = true;

            Process process;
            try
            {
                process = Process.Start(processInfo);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Process.Start: " + ex.Message);
                return new StatusCodeResult((int)HttpStatusCode.InternalServerError);
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

            log.LogInformation($"Standard Output: {output}");
            log.LogInformation($"Standard Error: {err}");

            var outputDir = Path.Combine(zipDir, "converted");
            if (!Directory.Exists(outputDir))
            {
                log.LogError($"Output directory {outputDir} does not exist");
                return new StatusCodeResult((int)HttpStatusCode.InternalServerError);
            }

            log.LogInformation($"Output directory {outputDir}");
            string[] files = Directory.GetFiles(outputDir).Concat(Directory.GetDirectories(outputDir)).ToArray();
            foreach (var file in files)
            {
                log.LogInformation(file);
            }

            // If we want to write the zip to disk then we can use a FileStream here and also
            // replace the FileStreamResult with a PhysicalFileResult (see below)
            //
            // using (var stream = new FileStream(filePath, FileMode.OpenOrCreate))

            // The memory stream will be disposed by the FileStreamResult
            //
            var stream = new MemoryStream();

            // Ensure using true for the leaveOpen parameter otherwise the memory stream will
            // get disposed before w are done with it.
            //
            using (var OutputZip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                OutputZip.CreateEntryFromDirectory(zipDir);
            }

            // Rewind
            //
            stream.Position = 0;

            return new FileStreamResult(stream, System.Net.Mime.MediaTypeNames.Application.Zip)
            {
                FileDownloadName = ArchiveName
            };

            //result = new PhysicalFileResult(filePath, new MediaTypeHeaderValue("application/zip"))
            //{
            //    FileDownloadName = "output.zip"
            //};
        }

        private static async Task<PostData> ProcessParameters(HttpRequest req)
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

            return data;
        }
    }

    public class BlenderCommandProcessor
    {
        private const string CommandStart = "-b -P /local/scripts/";
        private const string CommandSeperator = "--";
        
    }
}
