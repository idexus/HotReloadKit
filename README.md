# HotReloadKit

Hot Reload Kit for VS2022 for Mac

# Usage 

```cs
using HotReloadKit;

class Program
{
    static void Main(string[] args)
    {
        CodeReloader.Init<Program>(HotReloadSupport.IdeIPs);        
        
        // you can specify additional names of requested types
        CodeReloader.RequestedTypeNamesHandler = () => new string[] { "HotReloadExample.MyClass" };

        CodeReloader.UpdateApplication = types =>
        {
            foreach (var type in types) 
                Console.WriteLine(type.FullName);
        };
          
        ...

    }
}  
```

# Submodules

This project uses git submodules.

```
git submodule update --init --recursive
```

# VS2022 for Mac

- extension [HotReloadKit.VSMac_0.3.0_beta.2.mpack](https://github.com/idexus/HotReloadKit/releases)

# Disclaimer

There is no official support. Use at your own risk.

# License

License MIT, Copyright (c) 2023 Pawel Krzywdzinski
