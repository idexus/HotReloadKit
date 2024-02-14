# HotReloadKit

HotReloadKit is an extension tailored for C# .NET Core projects, delivering efficient hot reloading capabilities. Compatible with Visual Studio Code and Visual Studio 2022 for Mac and Windows, as well as .NET MAUI projects, it's a crucial tool utilized by Sharp.UI, a library simplifying .NET MAUI development with fluent methods solely in C# code.

- [Sharp.UI GitHub Repository](https://github.com/idexus/Sharp.UI)

<a href="https://youtu.be/wxQr0p3lEg0" target="_blank">
 <img src="https://github.com/idexus/Sharp.UI/raw/main/doc/assets/ytscreen2.jpg" alt="Hot Reload Support" width="640" border="0" />
</a>

# Simple Usage 

```cs
using HotReloadKit;

class Program
{
    static void Main(string[] args)
    {
        HotReloader.Init<Program>(HotReloadSupport.IdeIPs);
        HotReloader.RequestAdditionalTypes = () => new string[] { "HotReloadExample.MyClass" };        
        HotReloader.UpdateApplication = dataList =>
        {
            foreach (var data in dataList) 
                Console.WriteLine($"{data.Type.FullName} isFromChangedFile: {data.IsFromChangedFile}");
        };

        Console.ReadLine();
    }
} 
```

# VS extensions

- [vsix package for VS Code](https://github.com/idexus/HotReloadKit/releases)
- [vsix package for VS2022 for Windows](https://github.com/idexus/HotReloadKit/releases)
- [mpack package for VS2022 for Mac](https://github.com/idexus/HotReloadKit/releases)

# Nuget package

Add it to your project

- [https://www.nuget.org/packages/HotReloadKit](https://www.nuget.org/packages/HotReloadKit)

# Disclaimer

There is no official support. Use at your own risk.

# License

Licensed under the MIT License. © 2023 Pawel Krzywdzinski

**Enjoy!**
