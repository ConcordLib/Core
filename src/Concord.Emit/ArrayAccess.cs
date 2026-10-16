namespace Concord;

/// <summary>
///     Indexing helpers a composed body calls in place of a multi-dimensional array's pseudo-methods,
///     which have no metadata row for a body-replacement backend to write a token for.
/// </summary>
public static class ArrayAccess {
    /// <summary>Replaces a <c>call T[,]::Get</c> the target module cannot name.</summary>
    /// <typeparam name="T">The array's element type.</typeparam>
    /// <param name="array">The array to read.</param>
    /// <param name="row">The first-dimension index.</param>
    /// <param name="column">The second-dimension index.</param>
    /// <returns>The element at that position.</returns>
    public static T Get<T>(T[,] array, int row, int column) {
        return array[row, column];
    }

    /// <summary>Replaces a <c>call T[,]::Set</c> the target module cannot name.</summary>
    /// <typeparam name="T">The array's element type.</typeparam>
    /// <param name="array">The array to write.</param>
    /// <param name="row">The first-dimension index.</param>
    /// <param name="column">The second-dimension index.</param>
    /// <param name="value">The element to store.</param>
    public static void Set<T>(T[,] array, int row, int column, T value) {
        array[row, column] = value;
    }

    /// <summary>Replaces a <c>call T[,]::Address</c> the target module cannot name.</summary>
    /// <typeparam name="T">The array's element type.</typeparam>
    /// <param name="array">The array to point into.</param>
    /// <param name="row">The first-dimension index.</param>
    /// <param name="column">The second-dimension index.</param>
    /// <returns>A reference to the element at that position.</returns>
    public static ref T Address<T>(T[,] array, int row, int column) {
        return ref array[row, column];
    }

    /// <summary>Replaces a <c>newobj T[,]::.ctor</c> the target module cannot name.</summary>
    /// <typeparam name="T">The array's element type.</typeparam>
    /// <param name="rows">The first-dimension length.</param>
    /// <param name="columns">The second-dimension length.</param>
    /// <returns>The new array, zero-based in both dimensions.</returns>
    public static T[,] New<T>(int rows, int columns) {
        return new T[rows, columns];
    }
}
