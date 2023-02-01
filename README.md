# HotReloadKit

Hot Reload Kit for C# projects for VS2022 for MAc

# Usage 

```cs
using HotReloadKit;

class Program
{
    static void Main(string[] args)
    {
        CodeReloader.Init<Program>(HotReloadSupport.IdeIPs);        
        CodeReloader.RequestedTypeNamesHandler = () => new string[] { "HotReloadExample.MyClass" }; // aditional requested type names
        CodeReloader.UpdateApplication = types =>
        {
            foreach (var type in types) 
                Console.WriteLine(type.FullName);
        };
          
        Console.WriteLine("Hot Reload Test!");
        Console.ReadLine();
    }
}  
```

# VS2022 for Mac Extension

Use `HotReloadKit.VSMac_0.3.0.mpack`

# Disclaimer

__HotReloadKit__ is a proof of concept. There is no official support. Use at your own risk.

# License

License MIT, Copyright (c) Pawel Krzywdzinski
