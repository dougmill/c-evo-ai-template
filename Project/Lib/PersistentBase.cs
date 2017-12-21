using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using AI;
using CevoAILib;

namespace CevoAILib
{
    /// <summary>
    /// Class to convert batches of already-serialized data into sets of commands stored in the savegame.
    /// </summary>
    unsafe class Chunkinizer
    {
        private readonly AEmpire TheEmpire;

        public Chunkinizer(AEmpire empire) => TheEmpire = empire;

        /// <summary>
        /// Splits the given input data into 60 byte chunks and sends them to the server with cExStoreDataChunk commands
        /// as described in that command's comments, finishing off with the command passed in and a final few bits of
        /// data specific to that command.
        /// </summary>
        public void StoreAsChunks(int finishingCommand, int[] finishingData, int[][] data)
        {
            Debug.Assert(finishingData.Length <= 15);
            Debug.Assert((finishingCommand & Protocol.cDataSizeMask) == 0);
            int[] buffer = new int[15];
            fixed (int* ptr = buffer)
            {
                int nextBufferIndex = 0;
                foreach (int[] obj in data)
                {
                    buffer[nextBufferIndex] = obj.Length;
                    nextBufferIndex++;
                    int dataStored = 0;
                    while (dataStored < obj.Length)
                    {
                        int chunkSize = Math.Min(obj.Length - dataStored, buffer.Length - nextBufferIndex);
                        Array.Copy(obj, dataStored, buffer, nextBufferIndex, chunkSize);
                        dataStored += chunkSize;
                        nextBufferIndex += chunkSize;
                        if (nextBufferIndex == 15)
                        {
                            nextBufferIndex = 0;
                            TheEmpire.Play(Protocol.cExStoreDataChunk, 0, ptr);
                        }
                    }
                }

                if (nextBufferIndex + finishingData.Length > buffer.Length)
                {
                    Array.Clear(buffer, nextBufferIndex, buffer.Length - nextBufferIndex);
                    nextBufferIndex = 0;
                    TheEmpire.Play(Protocol.cExStoreDataChunk, 0, ptr);
                }

                Array.Copy(finishingData, 0, buffer, nextBufferIndex, finishingData.Length);
                TheEmpire.Play(finishingCommand + nextBufferIndex + finishingData.Length, 0, ptr);
            }
        }
    }

    /// <summary>
    /// The inverse of Chunkinizer. Receives chunks of data in sequence, whether from cExStoreDataChunk or the commands
    /// used to finish a batch, and combines them together so that other code can work only with the assembled arrays of
    /// arbitrary size, one array per stored object.
    /// </summary>
    unsafe class Accumulator
    {
        private readonly AEmpire TheEmpire;
        private readonly List<int[]> Buffers = new List<int[]>();
        private int StoredInLastBuffer = 0;

        public Accumulator(AEmpire empire) => TheEmpire = empire;

        /// <summary>
        /// Takes a pointer to a 15 int array, taken from the storage of a cExStoreDataChunk command, and adds those 15
        /// ints to an internal buffer, reading and splitting by the object lengths included in the data along the way.
        /// </summary>
        public void AddChunk(int* data)
        {
            AddChunk(ref data, 15);
        }

        private void AddChunk(ref int* data, int dataSize)
        {
            int amountProcessed = 0;
            if (Buffers.Count == 0)
            {
                Buffers.Add(new int[*data]);
                data++;
                amountProcessed = 1;
            }
            int[] buffer = Buffers.Last();
            while (amountProcessed < dataSize)
            {
                if (StoredInLastBuffer == buffer.Length)
                {
                    if (*data == 0)
                        break;
                    buffer = new int[*data];
                    Buffers.Add(buffer);
                    data++;
                    amountProcessed++;
                    StoredInLastBuffer = 0;
                }
                int amountToStore = Math.Min(dataSize - amountProcessed, buffer.Length - StoredInLastBuffer);
                Marshal.Copy((IntPtr) data, buffer, StoredInLastBuffer, amountToStore);
                data += amountToStore;
                amountProcessed += amountToStore;
                StoredInLastBuffer += amountToStore;
            }
        }

        /// <summary>
        /// Takes the final command in a batch, the number of command-specific fields there are, and a pointer to the
        /// command's data (length indicated by the last 4 bits of the command value as the server specifies), and
        /// finalizes a batch, returning the result. The returned data field is a jagged array of arrays, with each
        /// array containing the data for one of the arbitrary stored objects. The other returned field, finishingData,
        /// contains the command-specific additional information.
        /// </summary>
        public (int[][] data, int[] finishingData) Finish(int finishingCommand, int finishingDataLength, int* data)
        {
            int finalChunkSize = (finishingCommand & Protocol.cDataSizeMask) - finishingDataLength;
            AddChunk(ref data, finalChunkSize);
            int[] finishingData = new int[finishingDataLength];
            Marshal.Copy((IntPtr) data, finishingData, 0, finishingDataLength);
            int[][] dataArrays = Buffers.ToArray();
            Buffers.Clear();
            StoredInLastBuffer = 0;
            return (dataArrays, finishingData);
        }
    }

    /// <summary>
    /// Abstract base class for a dictionary of arbitrary size that is saved in the save game.
    /// </summary>
    /// <typeparam name="K">The key type of the dictionary.</typeparam>
    /// <typeparam name="V">The value type of the dictionary.</typeparam>
    /// <typeparam name="T">The implementing subclass.</typeparam>
    abstract unsafe class APersistentDictionary<K, V, T> : IDictionary<K, V> where T : APersistentDictionary<K, V, T>
    {
        protected static readonly IDictionary<Empire, IDictionary<int, T>> Index =
            new Dictionary<Empire, IDictionary<int, T>>();

        protected readonly Empire TheEmpire;
        private readonly int Id;
        private readonly IDictionary<K, V> WrappedDictionary;
        
        protected APersistentDictionary(Empire empire, int id)
        {
            TheEmpire = empire;
            Id = id;
            WrappedDictionary = new Dictionary<K, V>();
            if (!Index.TryGetValue(empire, out IDictionary<int, T> dict))
            {
                dict = new Dictionary<int, T>();
                Index[empire] = dict;
            }
            dict[id] = (T) this;
        }

        protected void Handle(int baseCommand, int[][] data)
        {
            switch (baseCommand)
            {
                case Protocol.cExCollectionClear:
                    WrappedDictionary.Clear();
                    break;
                case Protocol.cExDictRemoveItem:
                    var item = new KeyValuePair<K, V>(DeserializeKey(data[0]), DeserializeValue(data[1]));
                    WrappedDictionary.Remove(item);
                    break;
                case Protocol.cExDictRemoveKey:
                    WrappedDictionary.Remove(DeserializeKey(data[0]));
                    break;
                case Protocol.cExDictAddItem:
                    WrappedDictionary[DeserializeKey(data[0])] = DeserializeValue(data[1]);
                    break;

                default:
                    throw new Exception($"cClientEx base command {baseCommand:X} not handled.");
            }
        }

        protected abstract int ClassId { get; }
        protected abstract int[] SerializeKey(K key);
        protected abstract K DeserializeKey(int[] serialized);
        protected abstract int[] SerializeValue(V value);
        protected abstract V DeserializeValue(int[] serialized);

        public IEnumerator<KeyValuePair<K, V>> GetEnumerator() => WrappedDictionary.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable) WrappedDictionary).GetEnumerator();

        public void Add(KeyValuePair<K, V> item)
        {
            this[item.Key] = item.Value;
        }

        public void Clear()
        {
            fixed (int* data = new []{ClassId, Id})
            {
                TheEmpire.Play(Protocol.cExCollectionClear, 0, data);
            }
        }

        public bool Contains(KeyValuePair<K, V> item) => WrappedDictionary.Contains(item);

        public void CopyTo(KeyValuePair<K, V>[] array, int arrayIndex)
        {
            WrappedDictionary.CopyTo(array, arrayIndex);
        }

        public bool Remove(KeyValuePair<K, V> item)
        {
            if (!WrappedDictionary.Contains(item)) return false;

            int[][] data = {SerializeKey(item.Key), SerializeValue(item.Value)};
            TheEmpire.PersistData(Protocol.cExDictRemoveItem, new []{ClassId, Id}, data);

            return true;
        }

        public int Count => WrappedDictionary.Count;

        public bool IsReadOnly => false;

        public bool ContainsKey(K key) => WrappedDictionary.ContainsKey(key);

        public void Add(K key, V value)
        {
            this[key] = value;
        }

        public bool Remove(K key)
        {
            if (!WrappedDictionary.ContainsKey(key)) return false;

            TheEmpire.PersistData(Protocol.cExDictRemoveKey, new []{ClassId, Id}, new []{SerializeKey(key)});

            return true;
        }

        public bool TryGetValue(K key, out V value) => WrappedDictionary.TryGetValue(key, out value);

        public V this[K key]
        {
            get => WrappedDictionary[key];
            set
            {
                int[][] data = {SerializeKey(key), SerializeValue(value)};
                TheEmpire.PersistData(Protocol.cExDictAddItem, new []{ClassId, Id}, data);
            }
        }

        public ICollection<K> Keys => WrappedDictionary.Keys;

        public ICollection<V> Values => WrappedDictionary.Values;
    }

    /// <summary>
    /// Abstract base class for a list of arbitrary size that is saved in the save game. Requires value-based equality
    /// if relevant.
    /// </summary>
    /// <typeparam name="V">The type of the list elements.</typeparam>
    /// <typeparam name="T">The implementing subclass.</typeparam>
    abstract unsafe class APersistentList<V, T> : IList<V> where T : APersistentList<V, T>
    {
        protected static readonly IDictionary<Empire, IDictionary<int, T>> Index =
            new Dictionary<Empire, IDictionary<int, T>>();

        protected readonly Empire TheEmpire;
        private readonly int Id;
        private readonly IList<V> WrappedList;

        protected APersistentList(Empire empire, int id)
        {
            TheEmpire = empire;
            Id = id;
            WrappedList = new List<V>();
            if (!Index.TryGetValue(empire, out IDictionary<int, T> dict))
            {
                dict = new Dictionary<int, T>();
                Index[empire] = dict;
            }
            dict[id] = (T) this;
        }

        protected void Handle(int baseCommand, int index, int[][] data)
        {
            switch (baseCommand)
            {
                case Protocol.cExCollectionClear:
                    WrappedList.Clear();
                    break;
                case Protocol.cExListAdd:
                    WrappedList.Add(DeserializeValue(data[0]));
                    break;
                case Protocol.cExListRemove:
                    WrappedList.Remove(DeserializeValue(data[0]));
                    break;
                case Protocol.cExListInsert:
                    WrappedList.Insert(index, DeserializeValue(data[0]));
                    break;
                case Protocol.cExListRemoveAt:
                    WrappedList.RemoveAt(index);
                    break;
                case Protocol.cExListSet:
                    WrappedList[index] = DeserializeValue(data[0]);
                    break;

                default:
                    throw new Exception($"cClientEx base command {baseCommand:X} not handled.");
            }
        }

        protected abstract int ClassId { get; }
        protected abstract int[] SerializeValue(V value);
        protected abstract V DeserializeValue(int[] serialized);

        public IEnumerator<V> GetEnumerator() => WrappedList.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable) WrappedList).GetEnumerator();

        public void Add(V item)
        {
            TheEmpire.PersistData(Protocol.cExListAdd, new []{ClassId, Id}, new []{SerializeValue(item)});
        }

        public void Clear()
        {
            fixed (int* data = new []{ClassId, Id})
            {
                TheEmpire.Play(Protocol.cExCollectionClear, 0, data);
            }
        }

        public bool Contains(V item) => WrappedList.Contains(item);

        public void CopyTo(V[] array, int arrayIndex)
        {
            WrappedList.CopyTo(array, arrayIndex);
        }

        public bool Remove(V item)
        {
            int preRemoveCount = Count;
            TheEmpire.PersistData(Protocol.cExListRemove, new []{ClassId, Id}, new []{SerializeValue(item)});
            return preRemoveCount != Count;
        }

        public int Count => WrappedList.Count;

        public bool IsReadOnly => false;

        public int IndexOf(V item) => WrappedList.IndexOf(item);

        public void Insert(int index, V item)
        {
            TheEmpire.PersistData(Protocol.cExListInsert, new []{ClassId, Id, index}, new []{SerializeValue(item)});
        }

        public void RemoveAt(int index)
        {
            fixed (int* data = new []{ClassId, Id, index})
            {
                TheEmpire.Play(Protocol.cExListRemoveAt, 0, data);
            }
        }

        public V this[int index]
        {
            get => WrappedList[index];
            set =>
                TheEmpire.PersistData(Protocol.cExListSet, new []{ClassId, Id, index}, new []{SerializeValue(value)});
        }
    }

    /// <summary>
    /// Abstract base class for a set of arbitrary size that is saved in the save game. Requires value-based equality.
    /// </summary>
    /// <typeparam name="V">The type of the set elements.</typeparam>
    /// <typeparam name="T">The implementing subclass.</typeparam>
    abstract unsafe class APersistentSet<V, T> : ISet<V> where T : APersistentSet<V, T>
    {
        protected static readonly IDictionary<Empire, IDictionary<int, T>> Index =
            new Dictionary<Empire, IDictionary<int, T>>();

        protected readonly Empire TheEmpire;
        private readonly int Id;
        private readonly ISet<V> WrappedSet;

        protected APersistentSet(Empire empire, int id)
        {
            TheEmpire = empire;
            Id = id;
            WrappedSet = new HashSet<V>();
            if (!Index.TryGetValue(empire, out IDictionary<int, T> dict))
            {
                dict = new Dictionary<int, T>();
                Index[empire] = dict;
            }
            dict[id] = (T) this;
        }

        protected void Handle(int baseCommand, int[][] data)
        {
            switch (baseCommand)
            {
                case Protocol.cExCollectionClear:
                    WrappedSet.Clear();
                    break;
                case Protocol.cExSetAdd:
                    WrappedSet.Add(DeserializeValue(data[0]));
                    break;
                case Protocol.cExSetRemove:
                    WrappedSet.Remove(DeserializeValue(data[0]));
                    break;

                default:
                    throw new Exception($"cClientEx base command {baseCommand:X} not handled.");
            }
        }

        protected abstract int ClassId { get; }
        protected abstract int[] SerializeValue(V value);
        protected abstract V DeserializeValue(int[] serialized);

        public IEnumerator<V> GetEnumerator() => WrappedSet.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable) WrappedSet).GetEnumerator();

        public void Add(V item)
        {
            if (WrappedSet.Contains(item)) return;
            TheEmpire.PersistData(Protocol.cExSetAdd, new []{ClassId, Id}, new []{SerializeValue(item)});
        }

        public void UnionWith(IEnumerable<V> other)
        {
            foreach (V item in other)
                Add(item);
        }

        public void IntersectWith(IEnumerable<V> other)
        {
            if (!(other is ISet<V> toKeep))
                toKeep = new HashSet<V>(other);
            foreach (V item in WrappedSet)
                if (!toKeep.Contains(item))
                    TheEmpire.PersistData(Protocol.cExSetRemove, new []{ClassId, Id}, new []{SerializeValue(item)});
        }

        public void ExceptWith(IEnumerable<V> other)
        {
            foreach (V item in other)
                Remove(item);
        }

        public void SymmetricExceptWith(IEnumerable<V> other)
        {
            if (!(other is ISet<V> otherSet))
                otherSet = new HashSet<V>(other);
            foreach (V item in otherSet)
            {
                int command = Contains(item) ? Protocol.cExSetRemove : Protocol.cExSetAdd;
                TheEmpire.PersistData(command, new []{ClassId, Id}, new []{SerializeValue(item)});
            }
        }

        public bool IsSubsetOf(IEnumerable<V> other) => WrappedSet.IsSubsetOf(other);

        public bool IsSupersetOf(IEnumerable<V> other) => WrappedSet.IsSupersetOf(other);

        public bool IsProperSupersetOf(IEnumerable<V> other) => WrappedSet.IsProperSupersetOf(other);

        public bool IsProperSubsetOf(IEnumerable<V> other) => WrappedSet.IsProperSubsetOf(other);

        public bool Overlaps(IEnumerable<V> other) => WrappedSet.Overlaps(other);

        public bool SetEquals(IEnumerable<V> other) => WrappedSet.SetEquals(other);

        bool ISet<V>.Add(V item)
        {
            if (WrappedSet.Contains(item)) return false;
            TheEmpire.PersistData(Protocol.cExSetAdd, new []{ClassId, Id}, new []{SerializeValue(item)});
            return true;
        }

        public void Clear()
        {
            fixed (int* data = new []{ClassId, Id})
            {
                TheEmpire.Play(Protocol.cExCollectionClear, 0, data);
            }
        }

        public bool Contains(V item) => WrappedSet.Contains(item);

        public void CopyTo(V[] array, int arrayIndex)
        {
            WrappedSet.CopyTo(array, arrayIndex);
        }

        public bool Remove(V item)
        {
            if (!WrappedSet.Contains(item)) return false;
            TheEmpire.PersistData(Protocol.cExSetRemove, new []{ClassId, Id}, new []{SerializeValue(item)});
            return true;
        }

        public int Count => WrappedSet.Count;

        public bool IsReadOnly => false;
    }
}
