﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Dnx.Runtime;

namespace Microsoft.AspNet.Server.KestrelTests
{
    /// <summary>
    /// Summary description for Program
    /// </summary>
    public class Program
    {
        private readonly IApplicationEnvironment env;
        private readonly IServiceProvider sp;

        public Program(IApplicationEnvironment env, IServiceProvider sp)
        {
            this.env = env;
            this.sp = sp;
        }

        public int Main()
        {
            return new Xunit.Runner.AspNet.Program(env, sp).Main(new string[] {
                "-class",
                typeof(MultipleLoopTests).FullName
            });
        }
    }
}