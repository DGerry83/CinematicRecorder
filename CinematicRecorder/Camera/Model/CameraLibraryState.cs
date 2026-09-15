using System;
using System.Collections.Generic;
using CinematicRecorder.Camera.Persistence;

namespace CinematicRecorder.Camera.Model
{
    /// <summary>
    /// Result of a <see cref="CameraLibraryState.Add"/> attempt. Duplicates are rejected
    /// with a result (never an exception) so the P3 editor can map
    /// <see cref="NameAlreadyExists"/> to its inline "name taken" feedback.
    /// </summary>
    public enum CameraLibraryAddResult
    {
        /// <summary>The camera was appended to the library.</summary>
        Added,

        /// <summary>Rejected: another camera already carries that name (ordinal comparison).</summary>
        NameAlreadyExists,

        /// <summary>Rejected: the camera's name was null or empty.</summary>
        InvalidName,
    }

    /// <summary>
    /// The in-memory camera library: the service seam consumed by P1-C7 and, from P2/P3,
    /// by the UI (addendum §9 — the UI never touches the file or FlightCamera directly).
    ///
    /// Cameras are kept in an ordered list; creation order is preserved because P3's
    /// reorder ↑↓ depends on it. Name uniqueness is enforced here, not by the DTO.
    /// Persistence is delegated to <see cref="Persistence.CameraLibraryConfig"/>.
    ///
    /// This is the deliberate minimal P1 surface (Load/Save/Get/Add/Remove/enumerate);
    /// P3-C1 adds Copy/rename/reorder on top of the same storage and events, so those
    /// operations are additive and do not re-shape this API.
    /// </summary>
    public sealed class CameraLibraryState
    {
        private readonly List<CameraDefinition> _cameras = new List<CameraDefinition>();
        private readonly CameraLibraryConfig _persistence;

        /// <summary>
        /// Raised after any change to list membership or order: Load, Add, Remove today;
        /// P3-C1's copy/rename/reorder raise it as well. UI caches rebuild on this event
        /// only (cached-string discipline), never per frame.
        /// </summary>
        public event Action LibraryChanged;

        /// <summary>
        /// Raised when an existing camera's definition is edited in place (the write-through
        /// editing path). The minimal P1 surface has no camera-edit mutator — nothing in
        /// this chunk raises it; P3-C1's edit/rename implementation does. It is part of the
        /// seam now so P3 UI subscribers never re-wire.
        /// </summary>
        public event Action<CameraDefinition> CameraChanged;

        /// <summary>
        /// Creates an empty library bound to the in-game library file
        /// (<see cref="Persistence.CameraLibraryConfig.DefaultFilePath"/>).
        /// </summary>
        public CameraLibraryState() : this(new CameraLibraryConfig())
        {
        }

        /// <summary>
        /// Creates an empty library bound to an explicit persistence instance (verification
        /// harnesses pass an explicit-path <see cref="Persistence.CameraLibraryConfig"/>).
        /// </summary>
        /// <param name="persistence">The file I/O instance used by Load/Save.</param>
        public CameraLibraryState(CameraLibraryConfig persistence)
        {
            if (persistence == null) throw new ArgumentNullException(nameof(persistence));
            _persistence = persistence;
        }

        /// <summary>
        /// The cameras in creation (file) order. Live read-only view of the library;
        /// mutate via <see cref="Add"/>/<see cref="Remove"/> (and P3-C1's edit surface).
        /// </summary>
        public IReadOnlyList<CameraDefinition> Cameras
        {
            get { return _cameras; }
        }

        /// <summary>
        /// Finds a camera by name (ordinal comparison). Returns null when absent —
        /// mirrors the missing-library-entry rule: callers surface "Unavailable", never
        /// an exception.
        /// </summary>
        /// <param name="name">Camera name to look up; null returns null.</param>
        public CameraDefinition Get(string name)
        {
            if (name == null) return null;
            return _cameras.Find(c => string.Equals(c.Name, name, StringComparison.Ordinal));
        }

        /// <summary>
        /// Appends a camera to the library. Rejects null names and duplicates with a
        /// result — no exceptions for either. Fires <see cref="LibraryChanged"/> only on
        /// success.
        /// </summary>
        /// <param name="camera">The camera to add; must carry a unique, non-empty name.</param>
        public CameraLibraryAddResult Add(CameraDefinition camera)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            if (string.IsNullOrEmpty(camera.Name)) return CameraLibraryAddResult.InvalidName;
            if (Get(camera.Name) != null) return CameraLibraryAddResult.NameAlreadyExists;

            _cameras.Add(camera);
            LibraryChanged?.Invoke();
            return CameraLibraryAddResult.Added;
        }

        /// <summary>
        /// Removes a camera by name. Fires <see cref="LibraryChanged"/> only when a
        /// camera was actually removed.
        /// </summary>
        /// <param name="name">Name of the camera to remove.</param>
        /// <returns>True when a camera was removed; false when the name was unknown.</returns>
        public bool Remove(string name)
        {
            CameraDefinition camera = Get(name);
            if (camera == null) return false;

            _cameras.Remove(camera);
            LibraryChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Replaces the in-memory library with the contents of the library file (manual
        /// user action per parent spec §4.4 — no auto-load in P1). Malformed nodes are
        /// skipped by the persistence layer and counted in the result; this method never
        /// throws.
        /// </summary>
        /// <returns>The load outcome (loaded cameras + skipped count).</returns>
        public CameraLibraryLoadResult Load()
        {
            CameraLibraryLoadResult result = _persistence.Load();
            _cameras.Clear();
            _cameras.AddRange(result.Cameras);
            LibraryChanged?.Invoke();
            return result;
        }

        /// <summary>
        /// Writes the whole library to the library file in its current order.
        /// </summary>
        public void Save()
        {
            _persistence.Save(_cameras);
        }
    }
}
