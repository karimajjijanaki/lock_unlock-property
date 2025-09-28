using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PropertyLockingSystem.Hubs
{
    public class PropertyLockHub : Hub
    {
        // Concurrent dictionary to store locked properties: key = propertyId, value = userId
        private static ConcurrentDictionary<string, string> _lockedProperties = new ConcurrentDictionary<string, string>();

        // Lock object for atomic operations to prevent race conditions
        private static readonly object _lockObject = new object();

        public async Task LockProperty(string propertyId)
        {
            string connectionId = Context.ConnectionId; // Use connection ID as the unique identifier for the user/locker

            // Use lock to ensure atomic check and set operation
            // This fixes race conditions where multiple users could lock simultaneously
            lock (_lockObject)
            {
                if (_lockedProperties.ContainsKey(propertyId))
                {
                    // Property is already locked
                    await Clients.Caller.SendAsync("LockFailed", propertyId, "Property is already locked by another user.");
                    return;
                }

                // Check if this connection already owns this lock (prevent double-locking)
                if (_lockedProperties.TryGetValue(propertyId, out string ownerId) && ownerId == connectionId)
                {
                    await Clients.Caller.SendAsync("LockFailed", propertyId, "You already own this lock.");
                    return;
                }

                // Acquire the lock
                _lockedProperties[propertyId] = connectionId;
            }

            // Notify all clients about the lock
            await Clients.All.SendAsync("PropertyLocked", propertyId, connectionId);
        }

        public async Task UnlockProperty(string propertyId)
        {
            string connectionId = Context.ConnectionId; // Use connection ID as the unique identifier

            // Use lock for atomic operation
            lock (_lockObject)
            {
                if (_lockedProperties.TryGetValue(propertyId, out string ownerId) && ownerId == connectionId)
                {
                    _lockedProperties.TryRemove(propertyId, out _);
                }
                else
                {
                    // Not the owner or not locked
                    await Clients.Caller.SendAsync("UnlockFailed", propertyId, "You do not own this lock or it is not locked.");
                    return;
                }
            }

            // Notify all clients about the unlock
            await Clients.All.SendAsync("PropertyUnlocked", propertyId, connectionId);
        }

        public override async Task OnDisconnectedAsync(System.Exception exception)
        {
            // Release all locks held by the disconnecting connection
            // This ensures locks are released on unexpected disconnects (e.g., browser close, network loss), preventing orphaned locks
            string connectionId = Context.ConnectionId;

            List<string> propertiesToUnlock = new List<string>();

            lock (_lockObject)
            {
                foreach (var kvp in _lockedProperties.Where(kvp => kvp.Value == connectionId).ToList())
                {
                    propertiesToUnlock.Add(kvp.Key);
                    _lockedProperties.TryRemove(kvp.Key, out _);
                }
            }

            // Notify all clients about the released locks
            foreach (var propertyId in propertiesToUnlock)
            {
                await Clients.All.SendAsync("PropertyUnlocked", propertyId, connectionId);
            }

            await base.OnDisconnectedAsync(exception);
        }
    }
}
