using System.Runtime.CompilerServices;

// Grant the sibling Locks.Redis bridge package access to the internal
// `OrionGuardEfCoreOptions.ServiceCustomizations` hook so it can replace
// the registered IDistributedLock the same way the built-in
// UseDistributedLock<T>() does. Other future *.Locks.* backends will be
// added here as they ship.
[assembly: InternalsVisibleTo("Moongazing.OrionGuard.Locks.Redis")]
[assembly: InternalsVisibleTo("Moongazing.OrionGuard.Locks.Redis.Tests")]
