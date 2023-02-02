# HotReloadKit

Hot Reload Kit for C# projects for VS2022 for Mac

# Usage 

```cs
using HotReloadKit;

class Program
{
    static void Main(string[] args)
    {
        CodeReloader.Init<Program>(HotReloadSupport.IdeIPs);        
        
        // aditional requested type names
        CodeReloader.RequestedTypeNamesHandler = () => new string[] { "HotReloadExample.MyClass" };

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
# Submodules

This project uses git submodules.

```
git submodule update --init --recursive
```

# VS2022 for Mac Extension

Use `HotReloadKit.VSMac_0.3.0_beta.1.mpack`

# Disclaimer

There is no official support. Use at your own risk.

# License

License MIT, Copyright (c) Pawel Krzywdzinski
