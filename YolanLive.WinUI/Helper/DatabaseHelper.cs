using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

using System.IO;
using YolanLive.WinUI.Models;
using YolanLive.WinUI.ViewModels;

namespace YolanLive.WinUI.Helper
{

    public static class DatabaseHelper
    {
        static SqliteConnection db;
        public static Task InitializeDatabase(string dataDirectory = null)
        {
            var folder = Directory.CreateDirectory(dataDirectory ?? AppPaths.DataDirectory).FullName;
            string dbPath = Path.Combine(folder, "PlayWin.db");
            db?.Dispose();
            db = new SqliteConnection($"Filename={dbPath}");
            db.Open();
            string tableCommand = @"CREATE TABLE IF NOT EXISTS Favorite (
id INTEGER PRIMARY KEY AUTOINCREMENT, 
user_name TEXT,
site_name TEXT,
photo TEXT,
room_id TEXT);

CREATE TABLE IF NOT EXISTS History (
id INTEGER PRIMARY KEY AUTOINCREMENT, 
user_name TEXT,
site_name TEXT,
photo TEXT,
room_id TEXT,
watch_time DATETIME);
";
            using SqliteCommand createTable = new SqliteCommand(tableCommand, db);
            createTable.ExecuteNonQuery();

            return Task.CompletedTask;


        }

        public static void AddFavorite(FavoriteItem item)
        {
            if (!MainVM.Sites.Any(x => x.Name == item.SiteName)) { return; }
            if (CheckFavorite(item.RoomID, item.SiteName)!=null) { return; }
            using SqliteCommand command = new SqliteCommand();
            command.Connection = db;
            command.CommandText = "INSERT INTO Favorite VALUES (NULL,@user_name,@site_name, @photo, @room_id);";
            command.Parameters.AddWithValue("@user_name", item.UserName);
            command.Parameters.AddWithValue("@site_name", item.SiteName);
            command.Parameters.AddWithValue("@photo", item.Photo);
            command.Parameters.AddWithValue("@room_id", item.RoomID);
            command.ExecuteNonQuery();
        }
        public static long? CheckFavorite(string roomId, string siteName)
        {
            using SqliteCommand command = new SqliteCommand();
            command.Connection = db;
            command.CommandText = "SELECT * FROM Favorite WHERE room_id=@room_id and site_name=@site_name";
            command.Parameters.AddWithValue("@site_name", siteName);
            command.Parameters.AddWithValue("@room_id", roomId);
            var result = command.ExecuteScalar();
            if (result==null)
            {
                return null;
            }
            return (long)result;
        }
        public static void DeleteFavorite(long id)
        {
            using SqliteCommand command = new SqliteCommand();
            command.Connection = db;
            command.CommandText = "DELETE FROM Favorite WHERE id=@id";
            command.Parameters.AddWithValue("@id", id);
            command.ExecuteNonQuery();

        }

        public static void DeleteFavorite()
        {
            using SqliteCommand command = new SqliteCommand();
            command.Connection = db;
            command.CommandText = "DELETE FROM Favorite";
            command.ExecuteNonQuery();

        }

        public async static Task<List<FavoriteItem>> GetFavorites()
        {
            List<FavoriteItem> favoriteItems = new List<FavoriteItem>();
            using SqliteCommand command = new SqliteCommand("SELECT * FROM Favorite", db);
            using var reader =await command.ExecuteReaderAsync();
            while (reader.Read())
            {
                favoriteItems.Add(new FavoriteItem()
                {
                    ID= reader.GetInt32(0),
                    RoomID = reader.GetString(4),
                    Photo = reader.GetString(3),
                    SiteName = reader.GetString(2),
                    UserName = reader.GetString(1)
                });
            }
            return favoriteItems;
        }


        public static void AddHistory(HistoryItem item)
        {
            if (!MainVM.Sites.Any(x => x.Name == item.SiteName)) { return; }
            using SqliteCommand command = new SqliteCommand();
            command.Connection = db;
            var hisId = CheckHistory(item.RoomID, item.SiteName);
            if (hisId != null)
            {  
                //更新时间
                command.CommandText = "UPDATE History SET watch_time=@time WHERE room_id=@room_id and site_name=@site_name";
                command.Parameters.AddWithValue("@site_name", item.SiteName);
                command.Parameters.AddWithValue("@room_id", item.RoomID);
                command.Parameters.AddWithValue("@time", DateTime.Now);
                command.ExecuteNonQuery();
              
                return;
            }
          
            command.CommandText = "INSERT INTO History VALUES (NULL,@user_name,@site_name, @photo, @room_id,@time);";
            command.Parameters.AddWithValue("@user_name", item.UserName);
            command.Parameters.AddWithValue("@site_name", item.SiteName);
            command.Parameters.AddWithValue("@photo", item.Photo);
            command.Parameters.AddWithValue("@room_id", item.RoomID);
            command.Parameters.AddWithValue("@time", DateTime.Now);
            command.ExecuteNonQuery();
        }
        public static long? CheckHistory(string roomId, string siteName)
        {
            using SqliteCommand command = new SqliteCommand();
            command.Connection = db;
            command.CommandText = "SELECT * FROM History WHERE room_id=@room_id and site_name=@site_name";
            command.Parameters.AddWithValue("@site_name", siteName);
            command.Parameters.AddWithValue("@room_id", roomId);
            var result = command.ExecuteScalar();
            if (result == null)
            {
                return null;
            }
            return (long)result;
        }
        public static void DeleteHistory(long id)
        {
            using SqliteCommand command = new SqliteCommand();
            command.Connection = db;
            command.CommandText = "DELETE FROM History WHERE id=@id";
            command.Parameters.AddWithValue("@id", id);
            command.ExecuteNonQuery();
          
        }
        public static void DeleteHistory()
        {
            using SqliteCommand command = new SqliteCommand();
            command.Connection = db;
            command.CommandText = "DELETE FROM History";
            command.ExecuteNonQuery();

        }
        public async static Task<List<HistoryItem>> GetHistory()
        {
            List<HistoryItem> favoriteItems = new List<HistoryItem>();
            using SqliteCommand command = new SqliteCommand("SELECT * FROM History ORDER BY watch_time DESC", db);
            using var reader =await command.ExecuteReaderAsync();
            while (reader.Read())
            {
                favoriteItems.Add(new HistoryItem()
                {
                    ID= reader.GetInt32(0),
                    RoomID = reader.GetString(4),
                    Photo = reader.GetString(3),
                    SiteName = reader.GetString(2),
                    UserName = reader.GetString(1),
                    WatchTime= reader.GetDateTime(5)
                });
            }
            return favoriteItems;
        }

    }


}
