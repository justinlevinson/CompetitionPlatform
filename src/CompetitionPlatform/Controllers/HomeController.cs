﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CompetitionPlatform.Data.AzureRepositories.Project;
using CompetitionPlatform.Data.AzureRepositories.Result;
using CompetitionPlatform.Data.AzureRepositories.Settings;
using CompetitionPlatform.Data.AzureRepositories.Users;
using CompetitionPlatform.Data.ProjectCategory;
using CompetitionPlatform.Helpers;
using CompetitionPlatform.Models;
using Microsoft.AspNetCore.Mvc;
using CompetitionPlatform.Models.ProjectViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using CompetitionPlatform.Models.ProjectModels;
using Lykke.Service.PersonalData.Contract;

namespace CompetitionPlatform.Controllers
{
    public class HomeController : Controller
    {
        private readonly IProjectRepository _projectRepository;
        private readonly IProjectCommentsRepository _commentsRepository;
        private readonly IProjectCategoriesRepository _categoriesRepository;
        private readonly IProjectParticipantsRepository _participantsRepository;
        private readonly IProjectFollowRepository _projectFollowRepository;
        private readonly IProjectResultRepository _resultsRepository;
        private readonly IProjectWinnersRepository _winnersRepository;
        private readonly IUserFeedbackRepository _feedbackRepository;
        private readonly IUserRolesRepository _userRolesRepository;
        private readonly BaseSettings _settings;
        private readonly IPersonalDataService _personalDataService;

        public HomeController(IProjectRepository projectRepository, IProjectCommentsRepository commentsRepository,
            IProjectCategoriesRepository categoriesRepository, IProjectParticipantsRepository participantsRepository,
            IProjectFollowRepository projectFollowRepository, IProjectResultRepository resultsRepository,
            IProjectWinnersRepository winnersRepository, IUserFeedbackRepository feedbackRepository,
            IUserRolesRepository userRolesRepository, BaseSettings settings,
            IPersonalDataService personalDataService)
        {
            _projectRepository = projectRepository;
            _commentsRepository = commentsRepository;
            _categoriesRepository = categoriesRepository;
            _participantsRepository = participantsRepository;
            _projectFollowRepository = projectFollowRepository;
            _resultsRepository = resultsRepository;
            _winnersRepository = winnersRepository;
            _feedbackRepository = feedbackRepository;
            _userRolesRepository = userRolesRepository;
            _settings = settings;
            _personalDataService = personalDataService;
        }

        public async Task<IActionResult> Index()
        {
            var projectList = (await ProjectList.CreateProjectList(_projectRepository))
                .RemoveDrafts()
                .FilterByCurrentProjects(true)
                .OrderByLastModified();
                
            // fetch the view model
            var viewModel = await BuildViewModel(projectList);

            // fetch latest winners and JustFinishedProjects for the viewmodel
            viewModel.LatestWinners = await GetLatestWinners();
            viewModel.JustFinishedProjects = await GetJustFinishedProjects();

            // return view
            return View(viewModel);
        }

        public async Task<IActionResult> Allprojects(string status, string category, string prize)
        {
            // flip the flag for AllProjects, set MyProjects, FAQ, and Blog to false?
            ViewBag.AllProjects = ViewBag.AllProjects != true;
            ViewBag.MyProjects = false;
            ViewBag.Faq = false;
            ViewBag.Blog = false;

            var projectList = (await ProjectList.CreateProjectList(_projectRepository))
                .RemoveDrafts()
                .FilterByStatus(status)
                .FilterByCategory(category)
                .OrderByPrize(prize);

            var viewModel = await BuildViewModel(projectList);

            //TODO: Is this ordering needed? Status should be filtered, and prize should be ordered
            // order by status, then by budget, then by created
            //Default filtering - used when the page is first opened

            viewModel.Projects = viewModel.Projects
                .OrderBy(x => x.BaseProjectData.Status)
                .ThenByDescending(x => x.BaseProjectData.BudgetFirstPlace)
                .ThenBy(x => x.BaseProjectData.Created);
            return View(viewModel);
        }

        [Authorize]
        // "My Projects" is anything that the current user has created, participated in, or follows.
        // Create three separate ProjectLists, then combine them
        public async Task<IActionResult> Myprojects()
        {
            ViewBag.MyProjects = ViewBag.MyProjects != true;
            ViewBag.AllProjects = false;
            ViewBag.Faq = false;
            ViewBag.Blog = false;

            var user = GetAuthenticatedUser();

            var createdProjectList = (await ProjectList.CreateProjectList(_projectRepository))
                .FilterByAuthorId(user.Email);

            var participatingProjectList = await ProjectList.CreateProjectList(_projectRepository);
            participatingProjectList = await participatingProjectList.FilterByParticipating(user.Email, _participantsRepository);

            var followingProjectList = await ProjectList.CreateProjectList(_projectRepository);
            followingProjectList = await followingProjectList.FilterByFollowing(user.Email, _projectFollowRepository);

            var completeProjectList = createdProjectList
                .DistinctUnion(participatingProjectList)
                .DistinctUnion(followingProjectList)
                .RemoveDrafts()
                .OrderByLastModified();

            var viewModel = await BuildViewModel(completeProjectList);
           
            return View(viewModel);
        }

        public async Task<IActionResult> GetProjectList(string projectStatusFilter, string projectCategoryFilter, string projectPrizeFilter)
        {
            var projectList = (await ProjectList.CreateProjectList(_projectRepository))
                .RemoveDrafts()
                .FilterByStatus(projectStatusFilter)
                .FilterByCategory(projectCategoryFilter)
                .OrderByPrize(projectPrizeFilter);

            var viewModel = await BuildViewModel(projectList);
            return PartialView("ProjectListPartial", viewModel);
        }

        [Authorize]
        public async Task<IActionResult> GetMyProjectList(string myProjectStatusFilter, string myProjectCategoryFilter, string myProjectPrizeFilter)
        {
            var user = GetAuthenticatedUser();

            var createdProjectList = (await ProjectList.CreateProjectList(_projectRepository))
                .FilterByAuthorId(user.Email);

            var participatingProjectList = await ProjectList.CreateProjectList(_projectRepository);
            participatingProjectList = await participatingProjectList.FilterByParticipating(user.Email, _participantsRepository);

            var followingProjectList = await ProjectList.CreateProjectList(_projectRepository);
            followingProjectList = await followingProjectList.FilterByFollowing(user.Email, _projectFollowRepository);

            var completeProjectList = createdProjectList
                .DistinctUnion(participatingProjectList)
                .DistinctUnion(followingProjectList)
                .RemoveDrafts()
                .FilterByStatus(myProjectStatusFilter)
                .FilterByCategory(myProjectCategoryFilter)
                .OrderByPrize(myProjectPrizeFilter);

            var viewModel = await BuildViewModel(completeProjectList);
            return PartialView("ProjectListPartial", viewModel);
        }

        [Authorize]
        public async Task<IActionResult> GetFollowingProjects()
        {
            ViewBag.FollowingProjects = ViewBag.FollowingProjects != true;
            ViewBag.MyProjects = true;
            ViewBag.ParticipatingProjects = false;
            ViewBag.CreatedProjects = false;

            var user = GetAuthenticatedUser();

            var projectList = await ProjectList.CreateProjectList(_projectRepository);
            projectList = await projectList.FilterByFollowing(user.Email, _projectFollowRepository);
                
            var viewModel = await BuildViewModel(projectList);

            return View("Myprojects", viewModel);
        }

        [Authorize]
        public async Task<IActionResult> GetParticipatingProjects()
        {
            ViewBag.ParticipatingProjects = ViewBag.ParticipatingProjects != true;
            ViewBag.MyProjects = true;
            ViewBag.FollowingProjects = false;
            ViewBag.CreatedProjects = false;

            var user = GetAuthenticatedUser();

            var projectList = await ProjectList.CreateProjectList(_projectRepository);
            projectList = await projectList.FilterByParticipating(user.Email, _participantsRepository);

            var viewModel = await BuildViewModel(projectList);
            
            return View("Myprojects", viewModel);
        }

        [Authorize]
        public async Task<IActionResult> GetCreatedProjects()
        {
            ViewBag.CreatedProjects = ViewBag.CreatedProjects != true;
            ViewBag.MyProjects = true;
            ViewBag.FollowingProjects = false;
            ViewBag.ParticipatingProjects = false;

            var user = GetAuthenticatedUser();

            var projectList = (await ProjectList.CreateProjectList(_projectRepository))
                .FilterByAuthorId(user.Email);

            var viewModel = await BuildViewModel(projectList);
            
            return View("Myprojects", viewModel);
        }

        // Method to build the view model: fetch categories and compact the projects
        public async Task<ProjectListIndexViewModel> BuildViewModel(ProjectList projectList)
        {
            return new ProjectListIndexViewModel
            {
                ProjectCategories = _categoriesRepository.GetCategories(),
                Projects = await GetCompactProjectsList(projectList.GetProjects())
            };
        }
        
        private async Task<List<ProjectCompactViewModel>> GetCompactProjectsList(IEnumerable<IProjectData> projectList)
        {
            var compactModels = await CompactProjectList.CreateCompactProjectList(
                projectList,
                _commentsRepository,
                _participantsRepository,
                _projectFollowRepository,
                _resultsRepository,
                _winnersRepository,
                GetAuthenticatedUser().Email,
                _personalDataService
            );


            /* TODO: Move these from side effects to checks at object creation time*/
            foreach (var project in projectList)
            {
                // if the project has no author, use the ClaimsHelper to get the AuthorIdentifier from the AuthorID and 
                // fill it in
                //if (string.IsNullOrEmpty(project.AuthorIdentifier))
                //{
                //    project.AuthorIdentifier = await ClaimsHelper.GetUserIdByEmail(
                //        _settings.LykkeStreams.Authentication.Authority, _settings.LykkeStreams.Authentication.ClientId,
                //        project.AuthorId);
                //    await _projectRepository.UpdateAsync(project);
                //}

                // if the project is missing the enum status, fill it in from the string
                if (!string.IsNullOrEmpty(project.ProjectStatus))
                {
                    project.Status = StatusHelper.GetProjectStatusFromString(project.ProjectStatus);
                }
            }

            return compactModels.GetProjects();
        }

        [Authorize]
        public async Task<IActionResult> FilterFollowingProjects()
        {
            var user = GetAuthenticatedUser();

            var projectList = await ProjectList.CreateProjectList(_projectRepository);
            projectList = await projectList.FilterByFollowing(user.Email, _projectFollowRepository);

            var viewModel = await BuildViewModel(projectList);
           
            return View("~/Views/Home/Index.cshtml", viewModel);
        }

        [Authorize]
        public IActionResult LeaveFeedback()
        {
            return View();
        }

        [Authorize]
        public async Task<IActionResult> ViewFeedBack()
        {
            var user = GetAuthenticatedUser();
            var userRole = await _userRolesRepository.GetAsync(user.Email.ToLower());

            if (userRole == null) return View("AccessDenied");

            var feedback = await _feedbackRepository.GetFeedbacksAsync();
            feedback = feedback.OrderByDescending(x => x.Created);

            var viewModel = new FeedbackListViewModel
            {
                FeedbackList = feedback
            };

            return View(viewModel);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> SaveFeeback(FeedbackViewModel feedbackViewModel)
        {
            var user = GetAuthenticatedUser();
            feedbackViewModel.Email = user.Email;
            feedbackViewModel.Created = DateTime.UtcNow;

            await _feedbackRepository.SaveAsync(feedbackViewModel);
            return RedirectToAction("Index", "Home");
        }


        public IActionResult SignIn()
        {
            var redirectUrl = Request.Headers["Referer"].ToString();
            return Challenge(new AuthenticationProperties { RedirectUri = redirectUrl });
        }

        public IActionResult About()
        {
            ViewData["Message"] = "Your application description page.";

            return View();
        }

        public IActionResult Contact()
        {
            ViewData["Message"] = "Your contact page.";

            return View();
        }

        public IActionResult Error()
        {
            return View();
        }

        [HttpGet]
        public IActionResult LogOut()
        {
            var redirectUrl = Request.Headers["Referer"].ToString();

            if (User.Identity.IsAuthenticated)
            {
                return SignOut(new AuthenticationProperties { RedirectUri = "/"},
                    CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme);
            }

            if (!string.IsNullOrEmpty(redirectUrl))
            {
                return Redirect(redirectUrl);
            }

            return RedirectToAction("Index", "Home");
        }

        private CompetitionPlatformUser GetAuthenticatedUser()
        {
            return ClaimsHelper.GetUser(User.Identity);
        }

        public IActionResult AuthenticationFailed()
        {
            return View("AuthenticationFailed");
        }

        public IActionResult Terms()
        {
            return View();
        }

        public IActionResult Faq()
        {
            ViewBag.Faq = ViewBag.Faq != true;
            ViewBag.MyProjects = false;
            ViewBag.AllProjects = false;
            ViewBag.Blog = false;

            return View();
        }

        public string Version()
        {
            return GetCurrentVersion();
        }

        [HttpGet("~/api/isalive")]
        public string Get()
        {
            var response = new IsAliveResponse
            {
                Version =
                    Microsoft.Extensions.PlatformAbstractions.PlatformServices.Default.Application.ApplicationVersion,
                Env = Environment.GetEnvironmentVariable("ENV_INFO")
            };

            var formatSettings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            };

            return JsonConvert.SerializeObject(response);
        }

        public async Task<string> ActiveProjectsCount()
        {
            var projects = await _projectRepository.GetProjectsAsync();
            projects = projects.Where(x => x.ProjectStatus != Status.Initiative.ToString() &&
                                           x.ProjectStatus != Status.Archive.ToString() &&
                                           x.ProjectStatus != Status.Draft.ToString());

            return projects.Count().ToString();
        }

        private string GetCurrentVersion()
        {
            return typeof(HomeController).GetTypeInfo().Assembly.GetName().Version.ToString();
        }

        private async Task<List<LatestWinner>> GetLatestWinners()
        {
            var latestWinners = new List<LatestWinner>();

            var projects = await _projectRepository.GetProjectsAsync();
            var archiveProjects = projects.Where(x => x.ProjectStatus == Status.Archive.ToString()).OrderByDescending(x => x.VotingDeadline);

            foreach (var project in archiveProjects)
            {
                var winners = await _winnersRepository.GetWinnersAsync(project.Id);
                foreach (var winner in winners)
                {
                    if (string.IsNullOrEmpty(winner.WinnerIdentifier))
                    {
                        winner.ProjectId = project.Id;
                        winner.WinnerIdentifier = await ClaimsHelper.GetUserIdByEmail(
                            _settings.LykkeStreams.Authentication.Authority,
                            _settings.LykkeStreams.Authentication.ClientId, winner.WinnerId);

                        await _winnersRepository.UpdateAsync(winner);
                    }

                    if (winner.Budget != null)
                        latestWinners.Add(
                            new LatestWinner
                            {
                                Name = winner.FullName,
                                ProjectId = project.Id,
                                ProjectName = project.Name,
                                Amount = (double)winner.Budget,
                                Id = winner.WinnerIdentifier
                            });
                }
                if (latestWinners.Count >= 4) break;
            }

            var winnersIdList = latestWinners.Select(winner => winner.Id).ToList();
            var winnerAvatarUrls = await _personalDataService.GetClientAvatarsAsync(winnersIdList);

            foreach (var winner in latestWinners)
            {
                winner.AvatarUrl = winnerAvatarUrls[winner.Id];
            }

            return latestWinners.Take(4).ToList();
        }

        private async Task<List<JustFinishedProject>> GetJustFinishedProjects()
        {
            var justFinishedProjects = new List<JustFinishedProject>();

            var projects = await _projectRepository.GetProjectsAsync();
            var archiveProjects = projects.Where(x => x.ProjectStatus == Status.Archive.ToString()).OrderByDescending(x => x.VotingDeadline);

            foreach (var project in archiveProjects)
            {
                var winners = await _winnersRepository.GetWinnersAsync(project.Id);

                var amount = winners.Where(winner => winner.Budget != null).Sum(winner => (double)winner.Budget);

                if (amount != 0)
                {
                    justFinishedProjects.Add(
                        new JustFinishedProject
                        {
                            ProjectName = project.Name,
                            ProjectId = project.Id,
                            Amount = amount,
                            NumberOfWinners = winners.Count()
                        });
                }

                if (justFinishedProjects.Count == 4) break;
            }

            return justFinishedProjects;
        }
    }
}