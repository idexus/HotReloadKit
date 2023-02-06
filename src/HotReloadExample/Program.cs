using System;
using HotReloadKit;

namespace HotReloadExample;

class Program
{
    static void Main(string[] args)
    {
        HotReloader.Init<Program>(HotReloadSupport.IdeIPs);
        HotReloader.RequestAdditionalTypeNames = () => new string[] { "HotReloadExample.MyClass" };        
        HotReloader.UpdateApplication = types =>
        {
            foreach (var type in types) 
                Console.WriteLine(type.FullName);
        };

        Console.ReadLine();
    }
} 
     
class A  
{           
        
}


class B
{  
    
}  

class C
{
  
}