#if NETFRAMEWORK
namespace System.Runtime.CompilerServices
{
    // Нужен компилятору для record/init в .NET Framework.
    internal static class IsExternalInit { }
}
#endif
