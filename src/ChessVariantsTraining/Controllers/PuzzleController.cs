﻿using ChessDotNet;
using ChessVariantsTraining.Attributes;
using ChessVariantsTraining.DbRepositories;
using ChessVariantsTraining.MemoryRepositories;
using ChessVariantsTraining.Models;
using ChessVariantsTraining.Services;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Dynamic;
using System.Linq;

namespace ChessVariantsTraining.Controllers
{
    public class PuzzleController : CVTController
    {
        IPuzzlesBeingEditedRepository puzzlesBeingEdited;
        IPuzzleRepository puzzleRepository;
        IRatingUpdater ratingUpdater;
        IMoveCollectionTransformer moveCollectionTransformer;
        IPuzzleTrainingSessionRepository puzzleTrainingSessionRepository;
        ICounterRepository counterRepository;
        IGameConstructor gameConstructor;

        static readonly string[] supportedVariants = new string[] { "Atomic", "KingOfTheHill", "ThreeCheck", "Antichess", "Horde", "RacingKings" };

        public PuzzleController(IPuzzlesBeingEditedRepository _puzzlesBeingEdited,
            IPuzzleRepository _puzzleRepository,
            IUserRepository _userRepository,
            IRatingUpdater _ratingUpdater,
            IMoveCollectionTransformer _movecollectionTransformer,
            IPuzzleTrainingSessionRepository _puzzleTrainingSessionRepository,
            ICounterRepository _counterRepository,
            IPersistentLoginHandler _loginHandler,
            IGameConstructor _gameConstructor) : base(_userRepository, _loginHandler)
        {
            puzzlesBeingEdited = _puzzlesBeingEdited;
            puzzleRepository = _puzzleRepository;
            ratingUpdater = _ratingUpdater;
            moveCollectionTransformer = _movecollectionTransformer;
            puzzleTrainingSessionRepository = _puzzleTrainingSessionRepository;
            counterRepository = _counterRepository;
            gameConstructor = _gameConstructor;
        }

        [Route("/Puzzle")]
        public IActionResult Index()
        {
            return View();
        }

        [Route("/Puzzle/{variant:supportedVariantOrMixed}")]
        public IActionResult Train(string variant)
        {
            variant = Utilities.NormalizeVariantNameCapitalization(variant);
            ViewBag.LoggedIn = loginHandler.LoggedInUserId(HttpContext).HasValue;
            ViewBag.Variant = variant;
            return View("Train");
        }

        [Route("/Puzzle/Editor")]
        [Restricted(true, UserRole.NONE)]
        public IActionResult Editor()
        {
            return View();
        }

        [HttpPost]
        [Route("/Puzzle/Editor/RegisterPuzzleForEditing")]
        [Restricted(true, UserRole.NONE)]
        public IActionResult RegisterPuzzleForEditing(string fen, string variant)
        {
            variant = Utilities.NormalizeVariantNameCapitalization(variant);
            if (!Array.Exists(supportedVariants, x => x == variant))
            {
                return Json(new { success = false, error = "Unsupported variant." });
            }
            ChessGame game = gameConstructor.Construct(variant, fen);
            Puzzle puzzle = new Puzzle();
            puzzle.Game = game;
            puzzle.InitialFen = fen;
            puzzle.Variant = variant;
            puzzle.Author = loginHandler.LoggedInUserId(HttpContext).Value;
            puzzle.Solutions = new List<string>();
            do
            {
                puzzle.ID = Guid.NewGuid().GetHashCode();
            } while (puzzlesBeingEdited.Contains(puzzle.ID));
            puzzlesBeingEdited.Add(puzzle);
            return Json(new { success = true, id = puzzle.ID });
        }

        [HttpGet]
        [Route("/Puzzle/Editor/GetValidMoves/{id}")]
        [Restricted(true, UserRole.NONE)]
        public IActionResult GetValidMoves(string id)
        {
            int puzzleId;
            if (!int.TryParse(id, out puzzleId))
            {
                return Json(new { success = false, error = "The given ID is invalid." });
            }

            Puzzle puzzle = puzzlesBeingEdited.Get(puzzleId);
            if (puzzle == null)
            {
                return Json(new { success = false, error = "The given ID does not correspond to a puzzle." });
            }
            if (puzzle.Author != loginHandler.LoggedInUserId(HttpContext).Value)
            {
                return Json(new { success = false, error = "Only the puzzle author can access this right now." });
            }

            ReadOnlyCollection<Move> validMoves;
            if (puzzle.Game.IsWinner(Player.White) || puzzle.Game.IsWinner(Player.Black))
            {
                validMoves = new ReadOnlyCollection<Move>(new List<Move>()); 
            }
            else
            {
                validMoves = puzzle.Game.GetValidMoves(puzzle.Game.WhoseTurn);
            }
            Dictionary<string, List<string>> dests = moveCollectionTransformer.GetChessgroundDestsForMoveCollection(validMoves);
            return Json(new { success = true, dests = dests, whoseturn = puzzle.Game.WhoseTurn.ToString().ToLowerInvariant() });
        }

        [HttpPost]
        [Route("/Puzzle/Editor/SubmitMove")]
        [Restricted(true, UserRole.NONE)]
        public IActionResult SubmitMove(string id, string origin, string destination, string promotion = null)
        {
            int puzzleId;
            if (!int.TryParse(id, out puzzleId))
            {
                return Json(new { success = false, error = "The given ID is invalid." });
            }
            if (promotion != null && promotion.Length != 1)
            {
                return Json(new { success = false, error = "Invalid 'promotion' parameter." });
            }

            Puzzle puzzle = puzzlesBeingEdited.Get(puzzleId);
            if (puzzle == null)
            {
                return Json(new { success = false, error = "The given ID does not correspond to a puzzle." });
            }
            if (puzzle.Author != loginHandler.LoggedInUserId(HttpContext).Value)
            {
                return Json(new { success = false, error = "Only the puzzle author can access this right now." });
            }

            MoveType type = puzzle.Game.ApplyMove(new Move(origin, destination, puzzle.Game.WhoseTurn, promotion?[0]), false);
            if (type.HasFlag(MoveType.Invalid))
            {
                return Json(new { success = false, error = "The given move is invalid." });
            }
            return Json(new { success = true, fen = puzzle.Game.GetFen() });
        }

        [HttpPost("/Puzzle/Editor/NewVariation")]
        [Restricted(true, UserRole.NONE)]
        public IActionResult NewVariation(string id)
        {
            int puzzleId;
            if (!int.TryParse(id, out puzzleId))
            {
                return Json(new { success = false, error = "The given ID is invalid." });
            }

            Puzzle puzzle = puzzlesBeingEdited.Get(puzzleId);
            if (puzzle == null)
            {
                return Json(new { success = false, error = "The given ID does not correspond to a puzzle." });
            }
            if (puzzle.Author != loginHandler.LoggedInUserId(HttpContext).Value)
            {
                return Json(new { success = false, error = "Only the puzzle author can access this right now." });
            }

            puzzle.Game = gameConstructor.Construct(puzzle.Variant, puzzle.InitialFen);
            return Json(new { success = true, fen = puzzle.InitialFen });
        }

        [HttpPost]
        [Route("/Puzzle/Editor/Submit")]
        [Restricted(true, UserRole.NONE)]
        public IActionResult SubmitPuzzle(string id, string solution, string explanation)
        {
            int puzzleId;
            if (!int.TryParse(id, out puzzleId))
            {
                return Json(new { success = false, error = "The given ID is invalid." });
            }

            if (string.IsNullOrWhiteSpace(solution))
            {
                return Json(new { success = false, error = "There are no accepted variations." });
            }

            Puzzle puzzle = puzzlesBeingEdited.Get(puzzleId);
            if (puzzle == null)
            {
                return Json(new { success = false, error = string.Format("The given puzzle (ID: {0}) cannot be published because it isn't being created.", id) });
            }
            if (puzzle.Author != loginHandler.LoggedInUserId(HttpContext).Value)
            {
                return Json(new { success = false, error = "Only the puzzle author can access this right now." });
            }

            puzzle.Solutions = new List<string>(solution.Split(';').Where(x => !string.IsNullOrWhiteSpace(x)));
            if (puzzle.Solutions.Count == 0)
            {
                return Json(new { success = false, error = "There are no accepted variations." });
            }
            puzzle.Game = null;
            puzzle.ExplanationUnsafe = explanation;
            puzzle.Rating = new Rating(1500, 350, 0.06);
            puzzle.Reviewers = new List<int>();
            if (UserRole.HasAtLeastThePrivilegesOf(loginHandler.LoggedInUser(HttpContext).Roles, UserRole.PUZZLE_REVIEWER))
            {
                puzzle.InReview = false;
                puzzle.Approved = true;
                puzzle.Reviewers.Add(loginHandler.LoggedInUserId(HttpContext).Value);
            }
            else
            {
                puzzle.InReview = true;
                puzzle.Approved = false;
            }
            puzzle.DateSubmittedUtc = DateTime.UtcNow;
            puzzle.ID = counterRepository.GetAndIncrease(Counter.PUZZLE_ID);
            if (puzzleRepository.Add(puzzle))
            {
                return Json(new { success = true, link = Url.Action("TrainId", "Puzzle", new { id = puzzle.ID }) });
            }
            else
            {
                return Json(new { success = false, error = "Something went wrong." });
            }
        }

        [HttpGet]
        [Route("/Puzzle/{id:int}", Name = "TrainId")]
        public IActionResult TrainId(int id)
        {
            Puzzle p = puzzleRepository.Get(id);
            if (p == null)
            {
                return ViewResultForHttpError(HttpContext, new HttpErrors.NotFound("The given puzzle could not be found."));
            }
            ViewBag.Variant = p.Variant;
            return View("Train", p);
        }

        [HttpGet]
        [Route("/Puzzle/Train/GetOneRandomly/{variant:supportedVariantOrMixed}")]
        public IActionResult GetOneRandomly(string variant, string trainingSessionId = null)
        {
            variant = Utilities.NormalizeVariantNameCapitalization(variant);
            if (variant == "Mixed")
            {
                variant = supportedVariants[new Random().Next(0, supportedVariants.Length)];
            }
            List<int> toBeExcluded;
            double nearRating = 1500;
            int? userId = loginHandler.LoggedInUserId(HttpContext);
            if (userId.HasValue)
            {
                User u = userRepository.FindById(userId.Value);
                toBeExcluded = u.SolvedPuzzles;
                nearRating = u.Ratings[variant].Value;
            }
            else if (trainingSessionId != null)
            {
                toBeExcluded = puzzleTrainingSessionRepository.Get(trainingSessionId)?.PastPuzzleIds ?? new List<int>();
            }
            else
            {
                toBeExcluded = new List<int>();
            }
            Puzzle puzzle = puzzleRepository.GetOneRandomly(toBeExcluded, variant, loginHandler.LoggedInUserId(HttpContext));
            if (puzzle != null)
            {
                return Json(new { success = true, id = puzzle.ID });
            }
            else
            {
                return Json(new { success = true, allDone = true });
            }
        }

        [HttpPost]
        [Route("/Puzzle/Train/Setup")]
        public IActionResult SetupTraining(string id, string trainingSessionId = null)
        {
            int puzzleId;
            if (!int.TryParse(id, out puzzleId))
            {
                return Json(new { success = false, error = "Invalid puzzle ID." });
            }
            Puzzle puzzle = puzzleRepository.Get(puzzleId);
            if (puzzle == null)
            {
                return Json(new { success = false, error = "Puzzle not found." });
            }
            puzzle.Game = gameConstructor.Construct(puzzle.Variant, puzzle.InitialFen);
            PuzzleTrainingSession session;
            if (trainingSessionId == null)
            {
                string g;
                do
                {
                    g = Guid.NewGuid().ToString();
                } while (puzzleTrainingSessionRepository.ContainsTrainingSessionId(g));
                session = new PuzzleTrainingSession(g, gameConstructor);
                puzzleTrainingSessionRepository.Add(session);
            }
            else
            {
                session = puzzleTrainingSessionRepository.Get(trainingSessionId);
                if (session == null)
                {
                    return Json(new { success = false, error = "Puzzle training session ID not found." });
                }
            }
            session.Setup(puzzle);
            return Json(new
            {
                success = true,
                trainingSessionId = session.SessionID,
                author = userRepository.FindById(session.Current.Author).Username,
                fen = session.Current.InitialFen,
                dests = moveCollectionTransformer.GetChessgroundDestsForMoveCollection(session.Current.Game.GetValidMoves(session.Current.Game.WhoseTurn)),
                whoseTurn = session.Current.Game.WhoseTurn.ToString().ToLowerInvariant(),
                variant = puzzle.Variant
            });
        }

        [HttpPost]
        [Route("/Puzzle/Train/SubmitMove")]
        public IActionResult SubmitTrainingMove(string id, string trainingSessionId, string origin, string destination, string promotion = null)
        {
            PuzzleTrainingSession session = puzzleTrainingSessionRepository.Get(trainingSessionId);
            SubmittedMoveResponse response = session.ApplyMove(origin, destination, promotion);
            dynamic jsonResp = new ExpandoObject();
            if (response.Correct == 1 || response.Correct == -1)
            {
                int? loggedInUser = loginHandler.LoggedInUserId(HttpContext);
                if (loggedInUser.HasValue)
                {
                    ratingUpdater.AdjustRating(loggedInUser.Value, session.Current.ID, response.Correct == 1, session.CurrentPuzzleStartedUtc.Value, session.CurrentPuzzleEndedUtc.Value, session.Current.Variant);
                }
                jsonResp.rating = (int)session.Current.Rating.Value;
            }
            jsonResp.success = response.Success;
            jsonResp.correct = response.Correct;
            jsonResp.check = response.Check;
            if (response.Error != null) jsonResp.error = response.Error;
            if (response.FEN != null) jsonResp.fen = response.FEN;
            if (response.ExplanationSafe != null) jsonResp.explanation = response.ExplanationSafe;
            if (response.Play != null)
            {
                jsonResp.play = response.Play;
                jsonResp.fenAfterPlay = response.FenAfterPlay;
                jsonResp.checkAfterAutoMove = response.CheckAfterAutoMove;
            }
            if (response.Moves != null) jsonResp.dests = moveCollectionTransformer.GetChessgroundDestsForMoveCollection(response.Moves);
            if (response.ReplayFENs != null)
            {
                jsonResp.replayFens = response.ReplayFENs;
                jsonResp.replayChecks = response.ReplayChecks;
                jsonResp.replayMoves = response.ReplayMoves;
            }
            return Json(jsonResp);
        }
    }
}
