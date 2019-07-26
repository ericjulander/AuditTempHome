using System;
using LockedModulesAuditor;

namespace LockedModules
{
    class Program
    {
        static void Main(string[] args)
        {
            var LockedModuleAudit = new LockedModulesAudit();
            var msg =LockedModuleAudit.ExecuteAudit("51754");
            foreach(var auditmessage in msg)
                System.Console.WriteLine("  Audit Message: "+auditmessage.Message +"\n  Audit Status: "+ auditmessage.Status);
        }
    }
}
