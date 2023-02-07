# HotReloadKit

Hot Reload Kit for VS2022 for Mac and Windows

# Usage 

```cs
using HotReloadKit;

class Program
{
    static void Main(string[] args)
    {
        HotReloader.Init<Program>(HotReloadSupport.IdeIPs);
        HotReloader.RequestAdditionalTypes = () => new string[] { "HotReloadExample.MyClass" };        
        HotReloader.UpdateApplication = types =>
        {
            foreach (var type in types) 
                Console.WriteLine(type.FullName);
        };

        Console.ReadLine();
    }
} 
```

# VS2022 extensions

- [mpack package for VS for Mac](https://github.com/idexus/HotReloadKit/releases)
- [vsix package for VS for Windows](https://github.com/idexus/HotReloadKit/releases)

# Nuget package

Add it to your project

```
dotnet add package HotReloadKit
```

# Disclaimer

There is no official support. Use at your own risk.

# License

License MIT, Copyright (c) 2023 Pawel Krzywdzinski
