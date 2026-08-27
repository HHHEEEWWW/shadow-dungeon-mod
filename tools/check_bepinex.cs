using System;
using System.Reflection;
class Program {
    static void Main() {
        var asm = Assembly.LoadFrom(@"E:\trainer\BepInExManager\data\plugin-library\shadow-dungeon-e385\pmtb77unqe7tn\BepInEx\core\BepInEx.dll");
        foreach (var t in asm.GetExportedTypes()) {
            if (t.BaseType != null && (t.BaseType.Name.Contains("Plugin") || t.BaseType.Name.Contains("MonoBehaviour"))) {
                Console.WriteLine(t.FullName + " : " + t.BaseType.FullName);
            }
        }
    }
}
