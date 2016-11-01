﻿using System;
using System.Configuration;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using DOL.WHD.Section14c.Api.Filters;
using DOL.WHD.Section14c.Business;
using DOL.WHD.Section14c.Business.Services;
using DOL.WHD.Section14c.DataAccess.Identity;
using DOL.WHD.Section14c.Domain.Models;
using DOL.WHD.Section14c.Domain.ViewModels;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.Owin;
using Microsoft.Owin.Security;
using RestSharp;
using System.Security.Claims;
using System.Linq;
using System.Collections.Generic;
using System.Data.Entity;
using DOL.WHD.Section14c.Domain.Models.Identity;

namespace DOL.WHD.Section14c.Api.Controllers
{
    [AuthorizeHttps]
    [RoutePrefix("api/Account")]
    public class AccountController : ApiController
    {
        private const string LocalLoginProvider = "Local";
        private ApplicationUserManager _userManager;
        private ApplicationRoleManager _roleManager;

        public ApplicationUserManager UserManager
        {
            get
            {
                return _userManager ?? Request.GetOwinContext().GetUserManager<ApplicationUserManager>();
            }
        }
        
        public ApplicationRoleManager RoleManager
        {
            get
            {
                return _roleManager ?? Request.GetOwinContext().Get<ApplicationRoleManager>();
            }
        }

        // GET api/Account/UserInfo
        [Route("UserInfo")]
        public UserInfoViewModel GetUserInfo()
        {
            var identity = (ClaimsIdentity)User.Identity;
            var user = UserManager.FindByIdAsync(User.Identity.GetUserId()).Result;
            return new UserInfoViewModel
            {
                UserId = user.Id,
                Email = user.Email,
                Organizations = user.Organizations,
                Roles = identity.Claims
                    .Where(x => x.Type == ApplicationClaimTypes.Role)
                    .Select(x => new RoleViewModel() { Name = x.Value}),
                ApplicationClaims = identity.Claims
                    .Where(x => x.Type.StartsWith(ApplicationClaimTypes.ClaimPrefix) && Convert.ToBoolean(x.Value)) // Only Application Claims
                    .Select(x => x.Type)
            };
        }

        // POST api/Account/ChangePassword
        [AllowAnonymous]
        [Route("ChangePassword")]
        public async Task<IHttpActionResult> ChangePassword(ChangePasswordBindingModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }
            if (!User.Identity.IsAuthenticated && string.IsNullOrEmpty(model.Email))
            {
                return BadRequest("No username or bearer token provided");
            }

            string userId;
            ApplicationUser user;
            if (User.Identity.IsAuthenticated)
            {
                userId = User.Identity.GetUserId();
                user = await UserManager.FindByIdAsync(userId);
            }
            else
            {
                user = await UserManager.FindByEmailAsync(model.Email);
                if (await UserManager.IsLockedOutAsync(user.Id))
                {
                    return BadRequest(App_GlobalResources.LocalizedText.InvalidUserNameorPassword);
                }

                var validCredentials = await UserManager.FindAsync(user.UserName, model.OldPassword);
                if (validCredentials == null)
                {
                    // increment failed login attempt
                    if (await UserManager.GetLockoutEnabledAsync(user.Id))
                    {
                        await UserManager.AccessFailedAsync(user.Id);
                    }
                    return BadRequest(App_GlobalResources.LocalizedText.InvalidUserNameorPassword);
                }

                userId = user.Id;
            }
            IdentityResult result = await UserManager.ChangePasswordAsync(userId, model.OldPassword,
                model.NewPassword);
            
            if (!result.Succeeded)
            {
                return GetErrorResult(result);
            }

            user.LastPasswordChangedDate = DateTime.UtcNow;
            await UserManager.UpdateAsync(user);

            return Ok();
        }

        // POST api/Account/RemoveLogin
        [Route("RemoveLogin")]
        public async Task<IHttpActionResult> RemoveLogin(RemoveLoginBindingModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            IdentityResult result;

            if (model.LoginProvider == LocalLoginProvider)
            {
                result = await UserManager.RemovePasswordAsync(User.Identity.GetUserId());
            }
            else
            {
                result = await UserManager.RemoveLoginAsync(User.Identity.GetUserId(),
                    new UserLoginInfo(model.LoginProvider, model.ProviderKey));
            }

            if (!result.Succeeded)
            {
                return GetErrorResult(result);
            }

            return Ok();
        }


        // POST api/Account/Register
        [AllowAnonymous]
        [Route("Register")]
        public async Task<IHttpActionResult> Register(RegisterBindingModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Validate Recaptcha
            var reCaptchaVerfiyUrl = ConfigurationManager.AppSettings["ReCaptchaVerfiyUrl"];
            var reCaptchaSecretKey = ConfigurationManager.AppSettings["ReCaptchaSecretKey"];
            if (!string.IsNullOrEmpty(reCaptchaVerfiyUrl) && !string.IsNullOrEmpty(reCaptchaSecretKey))
            {
                var remoteIpAddress = Request.GetOwinContext().Request.RemoteIpAddress;
                var reCaptchaService = new ReCaptchaService(new RestClient(reCaptchaVerfiyUrl));

                var validationResults = reCaptchaService.ValidateResponse(reCaptchaSecretKey, model.ReCaptchaResponse, remoteIpAddress);
                if (validationResults != ReCaptchaValidationResult.Disabled && validationResults != ReCaptchaValidationResult.Success)
                {
                    ModelState.AddModelError("ReCaptchaResponse", new Exception("Unable to validate reCaptcha Response"));
                    return BadRequest(ModelState);
                }
            }

            // Add User
            var now = DateTime.UtcNow;
            var user = new ApplicationUser() { UserName = model.Email, Email = model.Email };
            user.Organizations.Add(new OrganizationMembership { EIN = model.EIN, IsAdmin = true, CreatedAt = now, LastModifiedAt = now, CreatedBy_Id = user.Id, LastModifiedBy_Id = user.Id });

            IdentityResult result = await UserManager.CreateAsync(user, model.Password);

            if (!result.Succeeded)
            {
                return GetErrorResult(result);
            }
            string code = await UserManager.GenerateEmailConfirmationTokenAsync(user.Id);

            var callbackUrl = new Uri(Url.Link("ConfirmEmailRoute", new { userId = user.Id, code }));

            await UserManager.SendEmailAsync(user.Id,
                                                    "Confirm your account",
                                                    "Please confirm your account by clicking <a href=\"" + callbackUrl + "\">here</a>");

            return Ok();
        }

        [Route("LogOut")]
        public async Task<IHttpActionResult> Logout()
        {
            string currentUserId = User.Identity.GetUserId();
            await UserManager.UpdateSecurityStampAsync(currentUserId);
            return Ok();
        }

        [AuthorizeClaims(ApplicationClaimTypes.GetAccounts)]
        [HttpGet]
        public async Task<IEnumerable<UserInfoViewModel>> GetAccounts()
        {
            return await UserManager.Users.Select(x => new UserInfoViewModel
            {
                UserId = x.Id,
                Email = x.Email,
                Organizations = x.Organizations,
                Roles = x.Roles.Select(r => new RoleViewModel { Id = r.RoleId, Name = r.Role.Name }),
                ApplicationClaims = x.Roles.SelectMany(y => y.Role.RoleFeatures)
                    .Where(u => u.Feature.Key.StartsWith(ApplicationClaimTypes.ClaimPrefix))
                    .Select(i => i.Feature.Key)
            }).ToListAsync();
        }

        [AuthorizeClaims(ApplicationClaimTypes.CreateAccount)]
        [HttpPost]
        public async Task<IHttpActionResult> CreateAccount(UserInfoViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Add User
            var user = new ApplicationUser() { UserName = model.Email, Email = model.Email };
            
            IdentityResult result = await UserManager.CreateAsync(user);
            if (!result.Succeeded)
            {
                return GetErrorResult(result);
            }

            // Add to Roles
            foreach (var role in model.Roles)
            {
                var newUser = UserManager.FindByNameAsync(model.Email);
                var addResult = await UserManager.AddToRoleAsync(newUser.Result.Id, role.Name);
                if (!addResult.Succeeded)
                {
                    return GetErrorResult(result);
                }
            }

            return Ok(result);
        }

        [AuthorizeClaims(ApplicationClaimTypes.ModifyAccount)]
        [HttpPost]
        [Route("{userId}")]
        public IHttpActionResult ModifyAccount(string userId, UserInfoViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }
            
            var user = UserManager.Users.Include("Roles.Role").SingleOrDefault(x => x.Id == userId);
            if (user == null)
            {
                return BadRequest("User not found.");
            }

            // Modify User
            if (user.Email != model.Email || user.UserName != model.Email)
            {
                user.Email = model.Email;
                user.UserName = model.Email;
                var userUpdated = UserManager.Update(user);

                if (!userUpdated.Succeeded)
                {
                    return GetErrorResult(userUpdated);
                }
            }

            // Add Roles
            foreach (var role in model.Roles)
            {
                if (user.Roles.All(x => x.Role.Name != role.Name))
                {
                    var addResult = UserManager.AddToRole(user.Id, role.Name);
                    if (!addResult.Succeeded)
                    {
                        return GetErrorResult(addResult);
                    }
                }
            }

            // Remove
            foreach (var role in user.Roles.ToList())
            {
                if (model.Roles.All(x => x.Name != role.Role.Name))
                {
                    var removeResult = UserManager.RemoveFromRole(user.Id, role.Role.Name);
                    if (!removeResult.Succeeded)
                    {
                        return GetErrorResult(removeResult);
                    }
                }
            }

            return Ok();
        }

        [AllowAnonymous]
        public HttpResponseMessage Options()
        {
            return new HttpResponseMessage { StatusCode = HttpStatusCode.OK };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _userManager != null)
            {
                _userManager.Dispose();
                _userManager = null;
            }

            base.Dispose(disposing);
        }

        #region Helpers

        private IAuthenticationManager Authentication
        {
            get { return Request.GetOwinContext().Authentication; }
        }

        private IHttpActionResult GetErrorResult(IdentityResult result)
        {
            if (result == null)
            {
                return InternalServerError();
            }

            if (!result.Succeeded)
            {
                if (result.Errors != null)
                {
                    foreach (string error in result.Errors)
                    {
                        ModelState.AddModelError("", error);
                    }
                }

                if (ModelState.IsValid)
                {
                    // No ModelState errors are available to send, so just return an empty BadRequest.
                    return BadRequest();
                }

                return BadRequest(ModelState);
            }

            return null;
        }

        #endregion
    }
}
