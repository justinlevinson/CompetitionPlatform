﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using AzureStorage.Blob;
using System.Security.Cryptography.X509Certificates;
using Common;

namespace CompetitionPlatform
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var sertConnString = Environment.GetEnvironmentVariable("CertConnectionString");

            if (string.IsNullOrWhiteSpace(sertConnString) || sertConnString.Length < 10)
            {

                var host = new WebHostBuilder()
                    .UseKestrel()
                    .UseContentRoot(Directory.GetCurrentDirectory())
                    .UseUrls("http://*:5000/")
                    .UseIISIntegration()
                    .UseStartup<Startup>()
                    .Build();

                host.Run();

            }
            else
            {
                var sertContainer = Environment.GetEnvironmentVariable("CertContainer");
                var sertFilename = Environment.GetEnvironmentVariable("CertFileName");
                var sertPassword = Environment.GetEnvironmentVariable("CertPassword");

                var certBlob = new AzureBlobStorage(sertConnString);
                var cert = certBlob.GetAsync(sertContainer, sertFilename).Result.ToBytes();

                var xcert = new X509Certificate2(cert, sertPassword);

                var host = new WebHostBuilder()
                    .UseKestrel(x =>
                    {
                        x.AddServerHeader = false;
                        x.Listen(IPAddress.Any, 443, listenOptions =>
                        {
                            listenOptions.UseHttps(xcert);
                            listenOptions.UseConnectionLogging();
                        });
                    })
                    .UseContentRoot(Directory.GetCurrentDirectory())
                    .UseUrls("https://*:443/")
                    .UseIISIntegration()
                    .UseStartup<Startup>()
                    .Build();

                host.Run();
            }
        }
    }
}
