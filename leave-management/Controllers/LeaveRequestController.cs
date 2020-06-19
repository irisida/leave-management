using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using leave_management.Contracts;
using leave_management.Data;
using leave_management.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace leave_management.Controllers
{
    [Authorize]
    public class LeaveRequestController : Controller
    {
        private readonly ILeaveRequestRepository _leaveRequestRepo;
        private readonly ILeaveTypeRepository _leaveTypeRepo;
        private readonly ILeaveAllocationRepository _allocationsRepo;
        private readonly IMapper _mapper;
        private readonly UserManager<Employee> _userManager;

        public LeaveRequestController(
            ILeaveRequestRepository leaveRequestRepository,
            ILeaveTypeRepository leaveTypeRepository,
            ILeaveAllocationRepository allocationRepository,
            IMapper mapper,
            UserManager<Employee> userManager
        )
        {
            _leaveRequestRepo = leaveRequestRepository;
            _leaveTypeRepo = leaveTypeRepository;
            _allocationsRepo = allocationRepository;
            _mapper = mapper;
            _userManager = userManager;
        }

        [Authorize(Roles = "Administrator")]
        // GET: LeaveRequestsController
        public ActionResult Index()
        {
            var leaveRequests = _leaveRequestRepo.FindAll();
            var leaveRequestsModel = _mapper.Map<List<LeaveRequestViewModel>>(leaveRequests);
            var model = new AdminViewLeaveRequestViewModel
            {
                TotalRequests = leaveRequestsModel.Count,
                ApprovedRequests = leaveRequestsModel.Count(q => q.Approved == true),
                PendingRequests = leaveRequestsModel.Count(q => q.Approved == null),
                RejectedRequests = leaveRequestsModel.Count(q => q.Approved == false),
                LeaveRequests = leaveRequestsModel
            };
            return View(model);
        }

        public ActionResult CancelRequest(int id)
        {
            try
            {
                var leaveRequest = _leaveRequestRepo.FindById(id);
                var allocation = _allocationsRepo.GetLeaveAllocationsByEmployeeAndType(leaveRequest.RequestingEmployeeId, leaveRequest.LeaveTypeId);
                var user = _userManager.GetUserAsync(User).Result;
                var userId = user.Id;

                int daysRequested = (int)(leaveRequest.EndDate - leaveRequest.StartDate).TotalDays;
                allocation.NumberOfDays += daysRequested;

                leaveRequest.Cancelled = true;
                leaveRequest.CancellationStaffId = userId;

                _leaveRequestRepo.Update(leaveRequest);
                _allocationsRepo.Update(allocation);

                return RedirectToAction("MyLeave");
            }
            catch (Exception)
            {
                return RedirectToAction("MyLeave");
            }

            
        }

        public ActionResult ApproveRequest(int id)
        {
            try
            {
                var leaveRequest = _leaveRequestRepo.FindById(id);
                var allocation = _allocationsRepo.GetLeaveAllocationsByEmployeeAndType(leaveRequest.RequestingEmployeeId, leaveRequest.LeaveTypeId);
                var user = _userManager.GetUserAsync(User).Result;

                int daysRequested = (int)(leaveRequest.EndDate - leaveRequest.StartDate).TotalDays;
                allocation.NumberOfDays -= daysRequested; 

                leaveRequest.Approved = true;
                leaveRequest.ApprovedById = user.Id;
                leaveRequest.DateActioned = DateTime.Now;

                _leaveRequestRepo.Update(leaveRequest);
                _allocationsRepo.Update(allocation);

                return RedirectToAction(nameof(Index));                
            }
            catch (Exception)
            {

                return RedirectToAction(nameof(Index), "Home");
            }
            
        }


        public ActionResult RejectRequest(int id)
        {
            try
            {
                var leaveRequest = _leaveRequestRepo.FindById(id);
                var user = _userManager.GetUserAsync(User).Result;

                leaveRequest.Approved = false;
                leaveRequest.ApprovedById = user.Id;
                leaveRequest.DateActioned = DateTime.Now;

                _leaveRequestRepo.Update(leaveRequest);

                return RedirectToAction(nameof(Index));                
            }
            catch (Exception)
            {

                return RedirectToAction(nameof(Index), "Home");
            }
        }

        public ActionResult MyLeave()
        {
            try
            {
                var user = _userManager.GetUserAsync(User).Result;
                var userId = user.Id;
                var allocations = _allocationsRepo.GetLeaveAllocationsByEmployee(userId);
                var employeeRequests = _leaveRequestRepo.GetLeaveRequestsByEmployee(userId);

                var employeeAllocationModel = _mapper.Map<List<LeaveAllocationViewModel>>(allocations);
                var employeeRequestsModel = _mapper.Map<List<LeaveRequestViewModel>>(employeeRequests);

                var model = new EmployeeViewLeaveRequestViewModel
                {
                    LeaveAllocations = employeeAllocationModel,
                    LeaveRequesrts = employeeRequestsModel
                };

                return View(model);
            }
            catch (Exception)
            {
                return RedirectToAction(nameof(Index), "Home");
            }
        }


        // GET: LeaveRequestsController/Details/
        public ActionResult Details(int id)
        {
            var leaveRequest = _leaveRequestRepo.FindById(id);
            var model = _mapper.Map<LeaveRequestViewModel>(leaveRequest);

            return View(model);
        }

        // GET: LeaveRequestsController/Create
        public ActionResult Create()
        {
            /* Load up the leave types from the database.
             * The selectlistitem is required for a dropdown
             * so we need to convert the leaveType types to 
             * selectListItem.
             */ 
            var leaveTypes = _leaveTypeRepo.FindAll();
            var leaveTypeItems = leaveTypes.Select(q => new SelectListItem { Text = q.Name, Value = q.Id.ToString() });
            var model = new CreateLeaveRequestViewModel
            {
                LeaveTypes = leaveTypeItems,
                StartDate = DateTime.Now,
                EndDate = DateTime.Now  
            };

            return View(model);
        }

        // POST: LeaveRequestsController/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(CreateLeaveRequestViewModel model)
        {
            
            try
            {
                /* Loads in the leaveTypes data again in the event of
                 * model evaluation returning a result that is not valid. 
                 * This allows us to return the view with model without
                 * returning with missing data which would be a poor UX.
                 */
                var leaveTypes = _leaveTypeRepo.FindAll();
                var leaveTypeItems = leaveTypes.Select(q => new SelectListItem { Text = q.Name, Value = q.Id.ToString() });

                model.LeaveTypes = leaveTypeItems;

                if (!ModelState.IsValid)
                {
                    return View(model);
                }
                
                /* Date selection validation 1: 
                 * Checks that the end date is not before the start date
                 * if so it returns to the view and doesn't process.
                 */
                if(DateTime.Compare(model.StartDate, model.EndDate) > 1 )
                {
                    ModelState.AddModelError("", "The end date cannot be before the start date.");
                    return View(model);
                }


                /* load up the: 
                 * user details 
                 * allocation type
                 * number of days requested
                 */ 
                var employee = _userManager.GetUserAsync(User).Result;
                var allocation = _allocationsRepo.GetLeaveAllocationsByEmployeeAndType(employee.Id, model.LeaveTypeId);
                int daysRequested = (int)(model.EndDate - model.StartDate).TotalDays;

                /* Date selection validation 2: 
                 * Checks that the end date is not before the start date
                 * if so it returns to the view and doesn't process.
                 */
                if (daysRequested > allocation.NumberOfDays)
                {
                    ModelState.AddModelError("", "Insufficient allocation exists to process the request");
                    return View(model);
                }

                var leaveRequestModel = new LeaveRequestViewModel
                {
                    RequestingEmployeeId = employee.Id,
                    LeaveTypeId = model.LeaveTypeId,
                    StartDate = model.StartDate,
                    EndDate = model.EndDate,
                    Approved = null,
                    Cancelled = false,
                    DateRequested = DateTime.Now.Date
                };

                var leaveRequest = _mapper.Map<LeaveRequest>(leaveRequestModel);
                var isSuccess = _leaveRequestRepo.Create(leaveRequest);

                if (!isSuccess)
                {
                    ModelState.AddModelError("", "Insufficient allocation exists to process the request"); 
                    return View(model);
                }

                return RedirectToAction("MyLeave");
            }
            catch
            {
                ModelState.AddModelError("", "Error sending new leave request");
                return View(model);
            }
        }

        // GET: LeaveRequestsController/Edit/5
        public ActionResult Edit(int id)
        {
            return View();
        }

        // POST: LeaveRequestsController/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(int id, IFormCollection collection)
        {
            try
            {
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }

        // GET: LeaveRequestsController/Delete/5
        public ActionResult Delete(int id)
        {
            return View();
        }

        // POST: LeaveRequestsController/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id, IFormCollection collection)
        {
            try
            {
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }
    }
}
