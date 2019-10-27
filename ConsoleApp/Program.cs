﻿using System;
using KoenZomers.Ring.Api;
using System.Configuration;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.IO;

namespace KoenZomers.Ring.RecordingDownload
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine();

            var appVersion = Assembly.GetExecutingAssembly().GetName().Version;

            Console.WriteLine("Ring Recordings Download Tool v{0}.{1}.{2} by Koen Zomers", new object[] { appVersion.Major, appVersion.Minor, appVersion.Build });
            Console.WriteLine();

            // Ensure arguments have been provided
            if (args.Length == 0)
            {
                DisplayHelp();
                Environment.Exit(1);
            }

            // Parse the provided arguments
            var configuration = ParseArguments(args);

            // Ensure we have the required configuration
            if(string.IsNullOrWhiteSpace(configuration.Username))
            {
                Console.WriteLine("-username is required");
                Environment.Exit(1);
            }

            if (string.IsNullOrWhiteSpace(configuration.Password))
            {
                Console.WriteLine("-password is required");
                Environment.Exit(1);
            }

            if (!configuration.StartDate.HasValue)
            {
                Console.WriteLine("-startdate or -lastdays is required");
                Environment.Exit(1);
            }

            // Connect to Ring
            Console.WriteLine("Connecting to Ring services");
            var session = new Session(configuration.Username, configuration.Password);

            try
            {
                // Authenticate
                Console.WriteLine("Authenticating");
                session.Authenticate().Wait();
            }
            catch(System.Net.WebException)
            {
                Console.WriteLine("Connection failed. Validate your credentials.");
                Environment.Exit(1);
            }

            // Retrieve all sessions
            Console.WriteLine($"Downloading {(string.IsNullOrWhiteSpace(configuration.Type) ? "all" : configuration.Type)} historical events between {configuration.StartDate.Value:dddd d MMMM yyyy HH:mm:ss} and {(configuration.EndDate.HasValue ? configuration.EndDate.Value.ToString("dddd d MMMM yyyy HH:mm:ss") : "now")}");
            var doorbotHistory = session.GetDoorbotsHistory(configuration.StartDate.Value, configuration.EndDate).Result;

            // If a specific Type has been provided, filter out all the ones that don't match this type
            if(!string.IsNullOrWhiteSpace(configuration.Type))
            {
                doorbotHistory = doorbotHistory.Where(h => h.Kind.Equals(configuration.Type, StringComparison.CurrentCultureIgnoreCase)).ToList();
            }

            Console.WriteLine($"{doorbotHistory.Count} item{(doorbotHistory.Count == 1 ? "" : "s")} found, downloading to {configuration.OutputPath}");

            for(var itemCount = 0; itemCount < doorbotHistory.Count; itemCount++)
            {
                var doorbotHistoryItem = doorbotHistory[itemCount];

                // If no valid date on the item, skip it and continue with the next
                if (!doorbotHistoryItem.CreatedAtDateTime.HasValue) continue;

                // Construct the filename and path where to save the file
                var downloadFileName = $"{doorbotHistoryItem.CreatedAtDateTime.Value:yyyy-MM-dd HH-mm-ss} ({doorbotHistoryItem.Id}).mp4";
                var downloadFullPath = Path.Combine(configuration.OutputPath, downloadFileName);                

                short attempt = 0;
                do
                {
                    attempt++;

                    Console.Write($"{itemCount + 1} - {downloadFileName}... ");

                    try
                    {
                        session.GetDoorbotHistoryRecording(doorbotHistoryItem, downloadFullPath).Wait();

                        Console.WriteLine($"done ({new FileInfo(downloadFullPath).Length / 1048576} MB)");
                        break;
                    }
                    catch (AggregateException e)
                    {
                        if (e.InnerException != null && e.InnerException.GetType() == typeof(System.Net.WebException) && ((System.Net.WebException)e.InnerException).Response != null)
                        {
                            var webException = (System.Net.WebException)e.InnerException;
                            var response = new StreamReader(webException.Response.GetResponseStream()).ReadToEnd();

                            Console.Write($"failed ({(e.InnerException != null ? e.InnerException.Message : e.Message)} - {response})");
                        }
                        else
                        {
                            Console.Write($"failed ({(e.InnerException != null ? e.InnerException.Message : e.Message)})");
                        }
                    }

                    if(attempt >= configuration.MaxRetries)
                    {
                        Console.WriteLine(". Giving up.");
                    }
                    else
                    {
                        Console.WriteLine($". Retrying {attempt + 1}/{configuration.MaxRetries}.");
                    }
                } while (attempt < configuration.MaxRetries);
            }

            Console.WriteLine("Done");

            Environment.Exit(0);
        }

        /// <summary>
        /// Parses all provided arguments
        /// </summary>
        /// <param name="args">String array with arguments passed to this console application</param>
        private static Configuration ParseArguments(IList<string> args)
        {
            var configuration = new Configuration
            {
                Username = ConfigurationManager.AppSettings["RingUsername"],
                Password = ConfigurationManager.AppSettings["RingPassword"],
                OutputPath = Environment.CurrentDirectory
            };

            if (args.Contains("-out"))
            {
                configuration.OutputPath = args[args.IndexOf("-out") + 1];
            }

            if (args.Contains("-username"))
            {
                configuration.Username = args[args.IndexOf("-username") + 1];
            }

            if (args.Contains("-password"))
            {
                configuration.Password = args[args.IndexOf("-password") + 1];
            }

            if (args.Contains("-type"))
            {
                configuration.Type = args[args.IndexOf("-type") + 1];
            }

            if (args.Contains("-lastdays"))
            {
                if (double.TryParse(args[args.IndexOf("-lastdays") + 1], out double lastDays))
                {
                    configuration.StartDate = DateTime.Now.AddDays(lastDays * -1);
                    configuration.EndDate = DateTime.Now;
                }
            }

            if (args.Contains("-startdate"))
            {
                if (DateTime.TryParse(args[args.IndexOf("-startdate") + 1], out DateTime startDate))
                {
                    configuration.StartDate = startDate;
                }
            }

            if (args.Contains("-enddate"))
            {
                if (DateTime.TryParse(args[args.IndexOf("-enddate") + 1], out DateTime endDate))
                {
                    configuration.EndDate = endDate;
                }
            }

            if (args.Contains("-retries"))
            {
                if (short.TryParse(args[args.IndexOf("-retries") + 1], out short maxRetries))
                {
                    configuration.MaxRetries = maxRetries;
                }
            }

            return configuration;
        }

        /// <summary>
        /// Shows the syntax
        /// </summary>
        private static void DisplayHelp()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("   RingRecordingDownload.exe -username <username> -password <password> [-out <folder location> -type <motion/ring/...> -lastdays X -startdate <date> -enddate <date>]");
            Console.WriteLine();
            Console.WriteLine("username: Username of the account to use to log on to Ring");
            Console.WriteLine("password: Password of the account to use to log on to Ring");
            Console.WriteLine("out: The folder where to store the recordings (optional, will use current directory if not specified)");
            Console.WriteLine("type: The type of events to store the recordings of, i.e. motion or ring (optional, will download them all if not specified)");
            Console.WriteLine("lastdays: The amount of days in the past to download recordings of (optional)");
            Console.WriteLine("startdate: Date and time from which to start downloading events (optional)");
            Console.WriteLine("enddate: Date and time until which to download events (optional, will use today if not specified)");
            Console.WriteLine("retries: Amount of retries on download failures (optional, will use 3 retries by default)");
            Console.WriteLine();
            Console.WriteLine("Example:");
            Console.WriteLine("   RingRecordingDownload.exe -username my@email.com -password mypassword -lastdays 7");
            Console.WriteLine("   RingRecordingDownload.exe -username my@email.com -password mypassword -lastdays 7 -retries 5");
            Console.WriteLine("   RingRecordingDownload.exe -username my@email.com -password mypassword -lastdays 7 -type ring");
            Console.WriteLine("   RingRecordingDownload.exe -username my@email.com -password mypassword -lastdays 7 -type ring -out c:\\recordings");
            Console.WriteLine("   RingRecordingDownload.exe -username my@email.com -password mypassword -startdate 12-02-2019 08:12:45");
            Console.WriteLine("   RingRecordingDownload.exe -username my@email.com -password mypassword -startdate 12-02-2019 08:12:45 -enddate 12-03-2019 10:53:12");
            Console.WriteLine();
        }
    }
}
