﻿using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sharp.UI.Generator
{
    [Generator]
    public class Generator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context) { }

        public void Execute(GeneratorExecutionContext context)
        {
            //Helpers.WaitForDebugger(context.CancellationToken);

            //------------- hot reload support ---------------

            if (!context.Compilation.AssemblyName.Equals("Sharp.UI"))
            {
                var builder = new StringBuilder();
                builder.AppendLine("//");
                builder.AppendLine("// <auto-generated>");
                builder.AppendLine("//");
                builder.Append($@"
using System;
using System.Net;

namespace {context.Compilation.Assembly.Name.Split('-').First()}
{{
    public static class HotReloadSupport
    {{

        public static IPAddress[] IdeIPs =
#if DEBUG
        {{");

                String strHostName = Dns.GetHostName();
                IPHostEntry iphostentry = Dns.GetHostEntry(strHostName);
                foreach (IPAddress ipaddress in iphostentry.AddressList.Where(e => e.GetAddressBytes().Count() == 4))
                {
                    var address = string.Join(", ", ipaddress.GetAddressBytes().Select(e => e.ToString()));
                    builder.Append($@"
            new IPAddress(new byte[] {{{address}}}),");
                }
                builder.AppendLine($@"
        }};
#else
        {{ }};
#endif
    }}
}}
");
                context.AddSource($"{context.Compilation.AssemblyName}.HotReloadSupport.g.cs", builder.ToString());
            }
        }
    }
}