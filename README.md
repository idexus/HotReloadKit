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

# VS2022 for Mac

- extension [HotReloadKit mpack](https://github.com/idexus/HotReloadKit/releases)
- nuget

```
dotnet add package HotReloadKit
```

# Disclaimer

There is no official support. Use at your own risk.

# License

License MIT, Copyright (c) 2023 Pawel Krzywdzinski
