﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FSharpLint.CrossDomain
{
    public class FSharpLintWorker : MarshalByRefObject, FSharpLint.Worker.IFSharpLintWorker
    {
        public FSharpLint.Worker.Result RunLint(string projectFile)
        {
            System.Console.WriteLine("\n\nCrossDomain RunLint");
            System.Console.Write(string.Join("\n", System.AppDomain.CurrentDomain.GetAssemblies().Select(x => x.FullName)));
            System.Console.WriteLine("\n\n");

            var fullPath = System.Reflection.Assembly.GetExecutingAssembly().Location;

            var directory = System.IO.Path.GetDirectoryName(fullPath);

            var setup = new System.AppDomainSetup { PrivateBinPath = directory, ApplicationBase = directory, DisallowBindingRedirects = true };

            var evidence = System.AppDomain.CurrentDomain.Evidence;

            var appDomain = System.AppDomain.CreateDomain("Lint Domain", evidence, setup);

            var worker = appDomain.CreateInstanceAndUnwrap("FSharpLint.Application", "FSharpLint.Application.RunLint+FSharpLintWorker") as FSharpLint.Worker.IFSharpLintWorker;

            return worker.RunLint(projectFile);
        }
    }
}
