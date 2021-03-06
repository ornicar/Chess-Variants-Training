﻿using ChessVariantsTraining.Configuration;
using ChessVariantsTraining.Models;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace ChessVariantsTraining.DbRepositories
{
    public class ReportRepository : IReportRepository
    {
        MongoSettings settings;
        IMongoCollection<Report> reportCollection;

        public ReportRepository(IOptions<Settings> appSettings)
        {
            settings = appSettings.Value.Mongo;
            GetCollection();
        }

        private void GetCollection()
        {
            MongoClient client = new MongoClient();
            reportCollection = client.GetDatabase(settings.Database).GetCollection<Report>(settings.ReportCollectionName);
        }

        public bool Add(Report report)
        {
            var found = reportCollection.Find(new BsonDocument("_id", new BsonString(report.ID)));
            if (found != null && found.Any()) return false;
            try
            {
                reportCollection.InsertOne(report);
            }
            catch (Exception e) when (e is MongoWriteException || e is MongoBulkWriteException)
            {
                return false;
            }
            return true;
        }

        public bool Handle(string reportId, string judgement)
        {
            UpdateDefinition<Report> updateDef = Builders<Report>.Update.Set("handled", true).Set("judgementAfterHandling", judgement);
            FilterDefinition<Report> filter = Builders<Report>.Filter.Eq("_id", reportId);
            UpdateResult updateResult = reportCollection.UpdateOne(filter, updateDef);
            return updateResult.IsAcknowledged && updateResult.MatchedCount != 0;
        }

        public List<Report> GetUnhandledByType(string type)
        {
            FilterDefinition<Report> filter = Builders<Report>.Filter.Eq("type", type) & Builders<Report>.Filter.Eq("handled", false);
            var found = reportCollection.Find(filter);
            if (found == null)
            {
                return new List<Report>();
            }
            return found.ToList();
        }

        public List<Report> GetUnhandledByTypes(IEnumerable<string> types)
        {
            FilterDefinition<Report> filter = Builders<Report>.Filter.In("type", types) & Builders<Report>.Filter.Eq("handled", false);
            return reportCollection.Find(filter).ToList();
        }

        public Report GetById(string id)
        {
            return reportCollection.Find(Builders<Report>.Filter.Eq("_id", id)).FirstOrDefault();
        }
    }
}
