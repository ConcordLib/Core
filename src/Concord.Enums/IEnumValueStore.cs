namespace Concord;

/// <summary>
///     Runtime-adapter-supplied storage for the extended enum id-to-value map. Concord owns the model
///     and the allocation rules; the adapter owns where the map lives.
/// </summary>
public interface IEnumValueStore {
    /// <summary>
    ///     Loads the stored map.
    /// </summary>
    /// <param name="map">The stored id-to-value pairs, empty when nothing is stored yet.</param>
    /// <returns><see langword="true" /> when a map was loaded.</returns>
    bool TryLoad(out IReadOnlyDictionary<string, long> map);

    /// <summary>
    ///     Stores the map. Concord calls this after every allocation pass.
    /// </summary>
    /// <param name="map">The id-to-value pairs to store, including ids no live declaration claims.</param>
    void Save(IReadOnlyDictionary<string, long> map);
}
