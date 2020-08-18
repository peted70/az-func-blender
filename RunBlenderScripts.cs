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

namespace AzFuncDocker
{
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
            var content = await new StreamReader(req.Body).ReadToEndAsync();    
            var postData = JsonConvert.DeserializeObject<PostData>(content);

            var http = new HttpClient();
            var response = await http.GetAsync(postData.InputZipUri);

            var za = new ZipArchive(await response.Content.ReadAsStreamAsync(), ZipArchiveMode.Read);
            var zipDir = "/tmp/objzip/" + Guid.NewGuid();
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

            Directory.SetCurrentDirectory("/tmp");
            var files = Directory.GetFiles(Directory.GetCurrentDirectory());
            log.LogInformation(string.Join(" ", files));

            try
            {
                Directory.SetCurrentDirectory("/usr/bin/");
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Set Directory: " + ex.Message);
            }

            files = Directory.GetFiles(Directory.GetCurrentDirectory());

            log.LogInformation(string.Join(" ", files));

            if (File.Exists("/usr/bin/blender-2.83.4-linux64/blender"))
                log.LogInformation("found exe");

            ProcessStartInfo processInfo = null;
            try 
            {
                var commandArguments = "-b -P objmat.py -- -i " +  ObjFilePathParameter + " -o " + OutputFormatParameter;
                processInfo = new ProcessStartInfo("/usr/bin/blender-2.83.4-linux64/blender", commandArguments);

                //processInfo = new ProcessStartInfo("/usr/bin/blender-2.83.4-linux64/blender", "--version");
            }
            catch (Exception ex)
            {
                log.LogError(ex, "ProcessStartInfo: " + ex.Message);
            }

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

            // Read the output (or the error)
            string output = process.StandardOutput.ReadToEnd();
            string err = process.StandardError.ReadToEnd();
            process.WaitForExit();

            string responseMessage = "Standard Output: " + output;
            responseMessage += "\n\nStandard Error: " + err;

            // If we get this far we might have som ebinary output so write it to a zip archive and retun
            //
            var fs = new FileStream(Path.Combine(zipDir, "Ouput.zip"), FileMode.OpenOrCreate);
            var OutputZip = new ZipArchive(fs, ZipArchiveMode.Create);
            //OutputZip.CreateEntryFromFile()

            return new OkObjectResult(responseMessage);
        }
    }
}
