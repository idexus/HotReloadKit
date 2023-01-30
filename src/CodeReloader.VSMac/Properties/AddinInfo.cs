using System;
using Mono.Addins;
using Mono.Addins.Description;

[assembly:Addin (
    "CodeReloader.VSMac",
	Namespace = "CodeReloader.VSMac",
	Version = "0.1.0"
)]

[assembly:AddinName ("CodeReloader.VSMac")]
[assembly:AddinCategory ("IDE extensions")]
[assembly:AddinDescription ("CodeReloader.VSMac Hot Reload for Visual Studio for Mac")]
[assembly:AddinAuthor ("Pawel Krzywdzinski")]
