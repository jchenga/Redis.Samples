﻿using System;
using System.Collections.Specialized;
using System.Configuration;
using System.IO;
using System.Web;
using System.Web.Configuration;
using System.Web.Hosting;
using System.Web.Mvc;
using System.Web.SessionState;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace Redis.Web
{
    public class SessionItem
    {
        public string SessionId { get; set; }
        public DateTime Created { get; set; }
        public DateTime Expires { get; set; }
        public DateTime LockDate { get; set; }
        public int LockId { get; set; }
        public int Timeout { get; set; }
        public string SessionItems { get; set; }
        public bool Locked { get; set; }
        public int Flags { get; set; }
    }

    public class RedisSessionStateStoreProvider : SessionStateStoreProviderBase
    {
        private RedisConnectionProvider _redisConnectionProvider;
        private string _connectionString;
        private SessionStateSection _configSection;
        private string _applicationName;

        // initialize the provider
        public override void Initialize(string name, NameValueCollection config)
        {
           
            if (string.IsNullOrEmpty(name))
                name = "RedisSessionStateStore";

            if (String.IsNullOrEmpty(config["description"]))
            {
                config.Remove("description");
                config.Add("description", "Redis State Store Provider");

            }
            base.Initialize(name, config);

            _configSection = (SessionStateSection) ConfigurationManager.GetSection("system.web/sessionState");
            _connectionString = ConfigurationManager.ConnectionStrings["Redi.SessionState.Store"].ConnectionString;
            _redisConnectionProvider = new RedisConnectionProvider();
            _applicationName = HostingEnvironment.ApplicationVirtualPath;
        }

        private IDatabase Database {get { return _redisConnectionProvider.GetConnection(_connectionString).GetDatabase(); }}

        public override void Dispose()
        {
            _redisConnectionProvider.Dispose();
        }

        // return false because this session state provider does not support calling the Session_OnEnd event
        public override bool SetItemExpireCallback(SessionStateItemExpireCallback expireCallback)
        {
            return false;
        }

        public override void InitializeRequest(HttpContext context)
        {
           
        }

        public override SessionStateStoreData GetItem(HttpContext context, string id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
        {
            return GetSessionStoreItem(false, context, id, out locked, out lockAge, out lockId, out actions);
        }

        public override SessionStateStoreData GetItemExclusive(HttpContext context, string id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
        {
            return GetSessionStoreItem(true, context, id, out locked, out lockAge, out lockId, out actions);
        }

        public override void ReleaseItemExclusive(HttpContext context, string id, object lockId)
        {
            var cacheKey = BuildCachingKey(id);
            var cached = Database.StringGet(cacheKey);
            var item = DeserializeSessionItem(cached);

            if (item.LockId != (int) lockId)
                return;

            item.Locked = false;
            item.Expires = DateTime.UtcNow.AddMinutes(_configSection.Timeout.TotalMinutes);
            cached = SerializeSessionItem(item);

            Database.StringSet(cacheKey, cached);
        }

       

        public override void SetAndReleaseItemExclusive(HttpContext context, string id, SessionStateStoreData item, object lockId, bool newItem)
        {
            var items = Serialize((SessionStateItemCollection) item.Items);
            string cached = string.Empty;
            SessionItem sessionItem = null;
            string cacheKey = BuildCachingKey(id);

            if (newItem)
            {
               var utcNow = DateTime.UtcNow;

               sessionItem = new SessionItem
                {
                    SessionId = id,
                    Created = utcNow,
                    Expires = utcNow,
                    LockDate = utcNow,
                    LockId = 0,
                    Timeout = item.Timeout,
                    SessionItems = items,
                    Flags = (int)SessionStateActions.None
                };
            }
            else
            {
                cached = Database.StringGet(cacheKey);
                sessionItem = DeserializeSessionItem(cached);
                if (sessionItem.LockId != (int) lockId)
                    return;
                sessionItem.Expires = DateTime.UtcNow.AddMinutes(item.Timeout);
                sessionItem.Locked = false;
                sessionItem.SessionItems = items;
                sessionItem.LockId = (int) lockId;
               
            }

            cached = SerializeSessionItem(sessionItem);
            Database.StringSet(cacheKey, cached);
        }

        public override void RemoveItem(HttpContext context, string id, object lockId, SessionStateStoreData item)
        {
            var cacheKey = BuildCachingKey(id);
            var cached = Database.StringGet(cacheKey);
            if (cached.IsNullOrEmpty)
                return;
            var sessionItem = DeserializeSessionItem(cached);
            if (sessionItem.LockId == (int) lockId)
                Database.KeyDelete(cacheKey);
        }

        public override void ResetItemTimeout(HttpContext context, string id)
        {
            if (!Database.KeyExists(id))
                return;
            var cacheKey = BuildCachingKey(id);
            var cached = Database.StringGet(cacheKey);
            var sessionItem = DeserializeSessionItem(cached);

            sessionItem.Expires = DateTime.UtcNow.AddMinutes(_configSection.Timeout.TotalMinutes);
            cached = SerializeSessionItem(sessionItem);

            Database.StringSet(cacheKey, cached);
        }

        public override SessionStateStoreData CreateNewStoreData(HttpContext context, int timeout)
        {
            return new SessionStateStoreData(new SessionStateItemCollection(), 
                SessionStateUtility.GetSessionStaticObjects(context),
                timeout);
        }

        public override void CreateUninitializedItem(HttpContext context, string id, int timeout)
        {
            var utcNow = DateTime.UtcNow;

            var item = new SessionItem
            {
                SessionId = id,
                Created = utcNow,
                Expires = utcNow.AddMinutes(timeout),
                LockDate = utcNow,
                LockId = 0,
                Timeout = timeout,
                SessionItems = string.Empty,
                Flags = (int) SessionStateActions.InitializeItem
               
            };
            var serialized = SerializeSessionItem(item);
            var cacheKey = BuildCachingKey(id);
            Database.StringSet(cacheKey, serialized);
        }

        public override void EndRequest(HttpContext context)
        {
           
        }

        private SessionStateStoreData GetSessionStoreItem(bool lockRecord, HttpContext context,
            string id,
            out bool locked,
            out TimeSpan lockAge,
            out object lockId,
            out SessionStateActions actionFlags)
        {
            SessionItem cachedItem = null;
            
            SessionStateStoreData item = null;
            string sessionItems = null;
            int timeout = 0;
            bool deleteData = false;
            bool foundRecord = false;
            var cacheKey = BuildCachingKey(id);

            lockAge = TimeSpan.Zero;
            lockId = null;
            locked = false;
            actionFlags = SessionStateActions.None;
            string cached = Database.StringGet(cacheKey);
           
            if (string.IsNullOrEmpty(cached))
            {
                return item;
            }

            cachedItem = JsonConvert.DeserializeObject<SessionItem>(cached);

            if (lockRecord)
            {

                
                if (cachedItem.Locked != true && cachedItem.Expires > DateTime.UtcNow)
                {
                    cachedItem.Locked = true;
                    cachedItem.LockDate = DateTime.UtcNow;

                    cached = JsonConvert.SerializeObject(cachedItem);
                    Database.StringSet(cacheKey, cached);
                }
                else
                {
                    locked = true;
                }


            }

            var expires = cachedItem.Expires;
            if (expires < DateTime.UtcNow)
            {
                locked = false;
                deleteData = true;
            }
            else
            {
                foundRecord = true;
            }

            sessionItems = cachedItem.SessionItems;
            lockId = cachedItem.LockId;
            lockAge = DateTime.UtcNow.Subtract(cachedItem.LockDate);
            actionFlags = (SessionStateActions) cachedItem.Flags;
            timeout = cachedItem.Timeout;

            if (deleteData)
                Database.KeyDelete(cacheKey);

            
            if (foundRecord && !locked)
            {
                lockId = (int) lockId + 1;
                cachedItem.LockId = (int) lockId;
                cachedItem.Flags = (int) SessionStateActions.None;
                cached = JsonConvert.SerializeObject(cachedItem);
                Database.StringSet(cacheKey, cached);

                if (actionFlags == SessionStateActions.InitializeItem)
                    item = CreateNewStoreData(context, (int) _configSection.Timeout.TotalMinutes);
                else
                    item = Deserialize(context, sessionItems, timeout);
            }
            return item;
        }

        private string Serialize(SessionStateItemCollection items)
        {
                using (MemoryStream ms = new MemoryStream())
                using (BinaryWriter writer = new BinaryWriter(ms))
                {
                    if (items != null)
                        items.Serialize(writer);
                    return Convert.ToBase64String(ms.ToArray());
                }
        }

        private SessionStateStoreData Deserialize(HttpContext context,
            string serializedItems, int timeout)
        {
            using (MemoryStream ms = new MemoryStream(Convert.FromBase64String(serializedItems)))
            {
                SessionStateItemCollection sessionItems = new SessionStateItemCollection();

                if (ms.Length > 0)
                {
                    using (BinaryReader reader = new BinaryReader(ms))
                    {
                        sessionItems = SessionStateItemCollection.Deserialize(reader);
                    }
                }

               return new SessionStateStoreData(sessionItems, SessionStateUtility.GetSessionStaticObjects(context),
               timeout);
            }
        }

        private static SessionItem DeserializeSessionItem(RedisValue cached)
        {
            return JsonConvert.DeserializeObject<SessionItem>(cached);
        }

        private static string SerializeSessionItem(SessionItem item)
        {
            return JsonConvert.SerializeObject(item);
        }

        private string BuildCachingKey(string id)
        {
            return string.Format("{0}:{1}", _applicationName, id);
        }
    }
}