#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Moongazing.OrionGuard.Generators
{
    /// <summary>
    /// An immutable array compared by its elements. <see cref="ImmutableArray{T}"/> compares by reference,
    /// so a pipeline model holding one would never compare equal across two generator runs and every edit
    /// anywhere in the compilation would regenerate every output. Wrapping it keeps the models value-equal.
    /// </summary>
    internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
        where T : IEquatable<T>
    {
        private readonly ImmutableArray<T> _items;

        public EquatableArray(ImmutableArray<T> items)
        {
            _items = items;
        }

        private ImmutableArray<T> Items => _items.IsDefault ? ImmutableArray<T>.Empty : _items;

        public int Count => Items.Length;

        public bool Equals(EquatableArray<T> other) => Items.SequenceEqual(other.Items);

        public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                foreach (T item in Items)
                {
                    hash = (hash * 31) + item.GetHashCode();
                }

                return hash;
            }
        }

        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
