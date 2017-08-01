﻿using ACMESharp;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

namespace LetsEncrypt.ACME.Simple
{
    public class ManualPlugin : Plugin
    {
        public override string Name => "Manual";

        public override string ChallengeType => AcmeProtocol.CHALLENGE_TYPE_HTTP;

        public override List<Target> GetTargets()
        {
            var result = new List<Target>();

            return result;
        }

        public override List<Target> GetSites()
        {
            var result = new List<Target>();
            string[] lsDomains = new string[0];
            Target loTemp = new Target();
            
            if (Program.Options.San && !string.IsNullOrEmpty(Program.Options.ManualHost)) {
                lsDomains = Program.Options.ManualHost.Split(',');
                if (!(lsDomains == null) && lsDomains.Length <= Settings.maxNames) {
                    loTemp = new Target() {
                        Host = lsDomains[0],
                        WebRootPath = Program.Options.WebRoot,
                        PluginName = Name,
                        AlternativeNames = new List<string>(lsDomains)
                    };
                    result.Add(loTemp);
                    loTemp = null;

                } else {
                    Console.WriteLine(
                        $" You must specify at least one (but not more than {Settings.maxNames}) hosts for a SAN certificate.");
                    Log.Error(
                        $"You must specify at least one (but not more than {Settings.maxNames}) hosts for a SAN certificate.");
                }
            }
            return result;
        }

        public override void Install(Target target, string pfxFilename, X509Store store, X509Certificate2 certificate)
        {
            if (!string.IsNullOrWhiteSpace(Program.Options.Script) &&
                !string.IsNullOrWhiteSpace(Program.Options.ScriptParameters))
            {
                var parameters = string.Format(Program.Options.ScriptParameters, target.Host,
                    Properties.Settings.Default.PFXPassword,
                    pfxFilename, store.Name, certificate.FriendlyName, certificate.Thumbprint);
                Log.Information("Running {Script} with {parameters}", Program.Options.Script, parameters);
                Process.Start(Program.Options.Script, parameters);
            }
            else if (!string.IsNullOrWhiteSpace(Program.Options.Script))
            {
                Log.Information("Running {Script}", Program.Options.Script);
                Process.Start(Program.Options.Script);
            }
            else
            {
                Console.WriteLine(" WARNING: Unable to configure server software.");
            }
        }

        public override void Install(Target target)
        {
            // This method with just the Target paramater is currently only used by Centralized SSL
            if (!string.IsNullOrWhiteSpace(Program.Options.Script) &&
                !string.IsNullOrWhiteSpace(Program.Options.ScriptParameters))
            {
                var parameters = string.Format(Program.Options.ScriptParameters, target.Host,
                    Properties.Settings.Default.PFXPassword, Program.Options.CentralSslStore);
                Log.Information("Running {Script} with {parameters}", Program.Options.Script, parameters);
                Process.Start(Program.Options.Script, parameters);
            }
            else if (!string.IsNullOrWhiteSpace(Program.Options.Script))
            {
                Log.Information("Running {Script}", Program.Options.Script);
                Process.Start(Program.Options.Script);
            }
            else
            {
                Console.WriteLine(" WARNING: Unable to configure server software.");
            }
        }

        public override void Renew(Target target) {
            string[] lsDomains = new string[0];

            target.Valid = false;
            if (Program.ManualSanMode) {
                if (target.WebRootPath == Program.Options.WebRoot) {
                    lsDomains = Program.Options.ManualHost.Split(',');
                    if (lsDomains.Length > 0 && lsDomains.Length <= Settings.maxNames) {
                        //Check that they're the same domains
                        target.Valid = (target.Host == lsDomains[0] &&
                            string.Join(",", target.AlternativeNames.ToArray()) == Program.Options.ManualHost);
                    }
                }
            } else {
                //Check the renewal relates to the CLI's host and webroot
                target.Valid = (target.Host == Program.Options.ManualHost &&
                    target.WebRootPath == Program.Options.WebRoot);
            }

            if (target.Valid)
            {
                Console.WriteLine($" Processing Manual Certificate Renewal...");
                this.Auto(target);
            }
            else
            {
                Log.Error($"Target invalid.");
            }
        }

        public override void PrintMenu() {
            if (!String.IsNullOrEmpty(Program.Options.ManualHost)) {
                Target target = new Target()
                {
                    WebRootPath = Program.Options.WebRoot,
                    PluginName = Name
                };
                if (Program.Options.San)
                {
                    var domains = Program.Options.ManualHost.Split(',').ToList();
                    domains = domains.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToList();
                    target.Host = domains.FirstOrDefault();
                    target.AlternativeNames = domains.ToList();
                }
                else
                {
                    target.Host = Program.Options.ManualHost;
                }
                Auto(target);
                Environment.Exit(0);
            }
            Console.WriteLine(" M: Generate a certificate manually.");
        }

        public override void HandleMenuResponse(string response, List<Target> targets)
        {
            if (response == "m")
            {
                Console.Write("Enter a host name: ");
                var hostName = Console.ReadLine();
                string[] alternativeNames = null;
                List<string> sanList = null;

                if (Program.Options.San)
                {
                    Console.Write("Enter all Alternative Names seperated by a comma ");

                    // Copied from http://stackoverflow.com/a/16638000
                    int BufferSize = 16384;
                    Stream inputStream = Console.OpenStandardInput(BufferSize);
                    Console.SetIn(new StreamReader(inputStream, Console.InputEncoding, false, BufferSize));

                    // Include host in the list of DNS names passed to LE
                    var sanInput = hostName + "," + Console.ReadLine();
                    alternativeNames = sanInput.Split(',');
                    sanList = new List<string>(alternativeNames);
                }

                while (string.IsNullOrWhiteSpace(Program.Options.WebRoot))
                {
                    Console.Write("Enter a site path (the web root of the host for http authentication): ");
                    Program.Options.WebRoot = Console.ReadLine();
                }

                var allNames = new List<string>();
                allNames.Add(hostName);
                allNames.AddRange(sanList ?? new List<string>());
                allNames = allNames.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToList();

                if (allNames.Count < Settings.maxNames)
                {
                    Log.Error($"You entered too many hosts for a San certificate. Let's Encrypt currently has a maximum of {Settings.maxNames} alternative names per certificate.");
                    return;
                }
                if (allNames.Count == 0)
                {
                    Log.Error("No host names provided.");
                    return;
                }

                var target = new Target()
                {
                    Host = allNames.First(),
                    WebRootPath = Program.Options.WebRoot,
                    PluginName = Name,
                    AlternativeNames = allNames
                };
                Auto(target);
            }
        }

        public override void CreateAuthorizationFile(string answerPath, string fileContents)
        {
            Log.Information("Writing challenge answer to {answerPath}", answerPath);
            var directory = Path.GetDirectoryName(answerPath);
            Directory.CreateDirectory(directory);
            File.WriteAllText(answerPath, fileContents);
        }
    }
}