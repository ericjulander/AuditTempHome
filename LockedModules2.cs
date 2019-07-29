using CanvasCourseObjects;
using CanvasCourseObjects.CourseBlueprintSubscription;
using CanvasCourseObjects.CourseModule;
using HttpGrabberFunctions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
namespace LockedModulesAuditor2
{
    public enum AuditStatus { Pass, Fail, Warn, Facepalm };
    public class AuditMessage
    {
        public AuditStatus Status { get; set; }
        public string Message { get; set; }
        public string Url { get; set; }

    }
    public class LockedModulesAudit
    {

        private List<CanvasModule> GetModuleFromId(string id)
        {
            var canvas = new CanvasGrabber($"/api/v1/courses/{id}/modules");
            string res = canvas.GetAuthResponse(System.Environment.GetEnvironmentVariable("CANVAS_API_TOKEN")).Result;
            System.IO.File.WriteAllText($"./output/{id}_modules.json", res);
            return JsonConvert.DeserializeObject<List<CanvasModule>>(res);
        }

        private List<CanvasModule> GetBluePrintCourse(string id)
        {
            var canvas = new CanvasGrabber($"/api/v1/courses/{id}/blueprint_subscriptions");
            string res = canvas.GetAuthResponse(System.Environment.GetEnvironmentVariable("CANVAS_API_TOKEN")).Result;
            if (res.Equals("[]"))
                throw new Exception($"There are no blueprint subscriptions for the course {id} ! We have nothing to compare the course copy with!");
            res = res.Substring(1, res.Length - 2);
            var blueprint = JsonConvert.DeserializeObject<CanvasBlueprintSubscription>(res);
            return GetModuleFromId((blueprint.BlueprintCourse.Id.ToString()));
        }

        private delegate List<AuditMessage> AuditExecutor(List<AuditMessage> AuditMessages);
        private delegate AuditExecutor AuditFunction(List<CanvasModule> CourseCopy, List<CanvasModule> CourseBlueprint, string ID);
        private List<AuditMessage> RunSubAudits(string courseId, List<AuditFunction> SubAudits)
        {
            List<AuditMessage> auditMessages = new List<AuditMessage>();
            try
            {

                var CourseCopyModules = GetModuleFromId(courseId);
                var BlueprintModules = GetBluePrintCourse(courseId);
                do
                {
                    try
                    {
                        SubAudits[0](CourseCopyModules, BlueprintModules, courseId)(auditMessages);
                        SubAudits.RemoveAt(0);
                    }
                    catch (Exception e)
                    {
                        auditMessages.Add(GenerateMessage(courseId, $"Error: {e.Message}", AuditStatus.Facepalm));
                    }
                } while (SubAudits.Count > 0);
            }
            catch (Exception e)
            {
                auditMessages.Add(GenerateMessage(courseId, $"Error: {e.Message}", AuditStatus.Facepalm));

            }

            return auditMessages;
        }
        private AuditMessage GenerateMessage(string id, string message, AuditStatus status)
        {
            return new AuditMessage
            {
                Status = status,
                Message = message,
                Url = $"https://byui.instructure.com/api/v1/courses/{id}/modules"
            };
        }

        private AuditExecutor PipeMessages(List<AuditMessage> AuditMessages)
        {
            return (OtherMessages) =>
            {
                foreach (var message in AuditMessages)
                    OtherMessages.Add(message);
                return OtherMessages;
            };
        }

        private CanvasModule GetModuleFromId(string ID, List<CanvasModule> Modules)
        {
            var ModList = Modules.Where(module => module.Id.ToString() == ID);
            CanvasModule Mod = null;
            if (ModList.Count() > 0)
                Mod = ModList.ToList()[0];
            return Mod;
        }

        private string[] GetMissingPreReqs(CanvasModule copy, CanvasModule blueprint, List<CanvasModule> CourseCopy, List<CanvasModule> BlueprintCourse)
        {
            List<string> missingModules = new List<string>();
            foreach (var moduleId in blueprint.PrerequisiteModuleIds)
            {
                if (!Array.Exists(copy.PrerequisiteModuleIds, copyModuleID =>
                {
                    var CopyPreReqModule = GetModuleFromId(copyModuleID.ToString(), CourseCopy);
                    if (CopyPreReqModule == null)
                        return false;
                    var BlueprintPreReqModule = GetModuleFromId(moduleId.ToString(), BlueprintCourse);
                    if (BlueprintPreReqModule == null)
                        return false;
                    return BlueprintPreReqModule.Name.Equals(CopyPreReqModule.Name);
                })) 
                missingModules.Add(moduleId.ToString());
            }
            return missingModules.ToArray();
        }
        private string[] GetExtraPreReqs(CanvasModule copy, CanvasModule blueprint, List<CanvasModule> CourseCopy, List<CanvasModule> BlueprintCourse)
        {
            List<string> extraModules = new List<string>();
            foreach (var moduleId in copy.PrerequisiteModuleIds)
            {
                if (!Array.Exists(blueprint.PrerequisiteModuleIds, blueprintCopyId =>
                {
                    var CopyPreReqModule = GetModuleFromId(moduleId.ToString(), CourseCopy);
                    if (CopyPreReqModule == null)
                        return false;
                    var BlueprintPreReqModule = GetModuleFromId(blueprintCopyId.ToString(), BlueprintCourse);
                    if (BlueprintPreReqModule == null)
                        return false;
                    return CopyPreReqModule.Name.Equals(BlueprintPreReqModule.Name);
                }))
                extraModules.Add(moduleId.ToString());
            }
            return extraModules.ToArray();
        }
        private AuditExecutor ModulePrereqsMatch(List<CanvasModule> CourseCopy, List<CanvasModule> BlueprintCourse, string ID)
        {
            var AuditMessages = new List<AuditMessage>();
            if (CourseCopy.Count == BlueprintCourse.Count)
            {
                var CopyModules = CourseCopy.OrderBy(module => module.Name).ToList();
                var BlueprintModules = BlueprintCourse.OrderBy(module => module.Name).ToList();
                bool passed = true;
                for (var i = 0; i < CopyModules.Count; i++)
                {
                    var missingPrereqs = GetMissingPreReqs(CopyModules[i], BlueprintModules[i], CopyModules, BlueprintModules);
                    if (missingPrereqs.Length > 0)
                    {
                        passed = false;
                        var missing = "";
                        foreach (var prereq in missingPrereqs)
                        {
                            var Mod = GetModuleFromId(prereq, BlueprintModules);
                            if (Mod == null)
                                AuditMessages.Add(GenerateMessage(ID, $"The following module ID was added as a prerequsite, but this module could not be found in the course!\n     Module ID: {prereq}", AuditStatus.Warn));
                            missing += $"     {prereq}{(Mod != null ? " - " + Mod.Name : "")}\n";
                        }
                        AuditMessages.Add(GenerateMessage(ID, $"The module \"{CopyModules[i].Name}\" is missing the following prerequsites:\n{missing}", AuditStatus.Fail));
                    }
                    var extraPrereqs = GetExtraPreReqs(CopyModules[i], BlueprintModules[i], CopyModules, BlueprintModules);
                    if (extraPrereqs.Length > 0)
                    {
                        passed = false;
                        var extra = "";
                        foreach (var prereq in extraPrereqs)
                        {
                            var Mod = GetModuleFromId(prereq, CopyModules);
                            if (Mod == null)
                                AuditMessages.Add(GenerateMessage(ID, $"The following module ID was added as a prerequsite, but this module could not be found in the course!\n     Module ID: {prereq}", AuditStatus.Warn));
                            extra += $"     {prereq}{(Mod != null ? " - " + Mod.Name : "")}\n";
                        }
                        AuditMessages.Add(GenerateMessage(ID, $"The module \"{CopyModules[i].Name}\" has the following prerequsites which are not in the associated blueprint module:\n{extra}", AuditStatus.Fail));
                    }
                }
                if (passed)
                    AuditMessages.Add(GenerateMessage(ID, $"All prerequsites for the modules match", AuditStatus.Pass));

            }
            else
            {
                AuditMessages.Add(GenerateMessage(ID, "These two courses don't have the same number of modules. A comparison cannot be made.", AuditStatus.Fail));
            }

            return PipeMessages(AuditMessages);
        }

        private bool LockDatesMatch(CanvasModule Copy, CanvasModule BlueprintCourse)
        {
            bool NoUnlockDates = (Copy.UnlockAt == null && BlueprintCourse.UnlockAt == null);
            bool BothHaveUnlockDates = (Copy.UnlockAt != null && BlueprintCourse.UnlockAt != null);
            if (NoUnlockDates || BothHaveUnlockDates)
            {
                return true;
            }
            return false;
        }
        private AuditExecutor ModuleLockDatesMatch(List<CanvasModule> CourseCopy, List<CanvasModule> BlueprintCourse, string ID)
        {
            var AuditMessages = new List<AuditMessage>();
            if (CourseCopy.Count == BlueprintCourse.Count)
            {
                var CopyModules = CourseCopy.OrderBy(module => module.Name).ToList();
                var BlueprintModules = BlueprintCourse.OrderBy(module => module.Name).ToList();
                bool passed = true;
                for (var i = 0; i < CopyModules.Count; i++)
                {
                    if (!LockDatesMatch(CopyModules[i], BlueprintModules[i]))
                    {
                        passed = false;
                        AuditMessages.Add(GenerateMessage(ID, $"The module \"{CopyModules[i].Name}\" has an unmatching unlock date!", AuditStatus.Fail));
                    }
                }
                if (passed)
                    AuditMessages.Add(GenerateMessage(ID, $"All unlock dates for the modules match.", AuditStatus.Pass));

            }
            else
            {
                AuditMessages.Add(GenerateMessage(ID, "These two courses don't have the same number of modules. A comparison cannot be made.", AuditStatus.Fail));
            }

            return PipeMessages(AuditMessages);
        }

        private AuditExecutor Template(List<CanvasModule> CourseCopy, List<CanvasModule> BlueprintCourse, string ID)
        {
            var AuditMessages = new List<AuditMessage>();
            return PipeMessages(AuditMessages);
        }

        private bool SequentialProgressConfigurationsMatch(CanvasModule Copy, CanvasModule BlueprintCourse)
        {
            return Copy.RequireSequentialProgress == BlueprintCourse.RequireSequentialProgress;
        }
        private AuditExecutor ModuleSequentialProgressConfigurationsMatch(List<CanvasModule> CourseCopy, List<CanvasModule> BlueprintCourse, string ID)
        {
            var AuditMessages = new List<AuditMessage>();
            if (CourseCopy.Count == BlueprintCourse.Count)
            {
                var CopyModules = CourseCopy.OrderBy(module => module.Name).ToList();
                var BlueprintModules = BlueprintCourse.OrderBy(module => module.Name).ToList();
                bool passed = true;
                for (var i = 0; i < CopyModules.Count; i++)
                {
                    if (!SequentialProgressConfigurationsMatch(CopyModules[i], BlueprintModules[i]))
                    {
                        passed = false;
                        var requiresSequentialProgressMessage = BlueprintModules[i].RequireSequentialProgress ? "requires" : "does not require";
                        AuditMessages.Add(GenerateMessage(ID, $"The module \"{CopyModules[i].Name}\" does not match the same confifuration as the blueprint!\n This module {requiresSequentialProgressMessage} sequential progress.", AuditStatus.Fail));
                    }
                }
                if (passed)
                    AuditMessages.Add(GenerateMessage(ID, $"All modules have the same sequential progress configurations.", AuditStatus.Pass));

            }
            else
            {
                AuditMessages.Add(GenerateMessage(ID, "These two courses don't have the same number of modules. A comparison cannot be made.", AuditStatus.Fail));
            }

            return PipeMessages(AuditMessages);
        }

        private bool StatesMatch(CanvasModule Copy, CanvasModule BlueprintCourse)
        {
            if(Copy.State == null && BlueprintCourse.State == null)
                return true;
            else if(Copy.State == null || BlueprintCourse.State == null)
                return false;
            return Copy.State.Equals(BlueprintCourse.State);
        }
        private AuditExecutor ModuleStatesMatch(List<CanvasModule> CourseCopy, List<CanvasModule> BlueprintCourse, string ID)
        {
            var AuditMessages = new List<AuditMessage>();
            if (CourseCopy.Count == BlueprintCourse.Count)
            {
                var CopyModules = CourseCopy.OrderBy(module => module.Name).ToList();
                var BlueprintModules = BlueprintCourse.OrderBy(module => module.Name).ToList();
                bool passed = true;
                for (var interval = 0; interval < CopyModules.Count; interval++)
                {
                    if (!StatesMatch(CopyModules[interval], BlueprintModules[interval]))
                    {
                        passed = false;
                        var requiresSequentialProgressMessage = BlueprintModules[interval].RequireSequentialProgress ? "requires" : "does not require";
                        AuditMessages.Add(GenerateMessage(ID, $"The module state of \"{CopyModules[interval].Name}\" does not match the same confifuration as the blueprint!\n This module state should be \"{BlueprintModules[interval].State}\" not \"{CopyModules[interval].State}\".", AuditStatus.Fail));
                    }
                    
                }
                if (passed)
                    AuditMessages.Add(GenerateMessage(ID, $"All modules have matching states", AuditStatus.Pass));

            }
            else
            {
                AuditMessages.Add(GenerateMessage(ID, "These two courses don't have the same number of modules. A comparison cannot be made.", AuditStatus.Fail));
            }

            return PipeMessages(AuditMessages);
        }

        public List<AuditMessage> ExecuteAudit(string courseCode)
        {
            var ops = new List<AuditFunction>(){
               ModulePrereqsMatch,
               ModuleLockDatesMatch,
               ModuleSequentialProgressConfigurationsMatch,
               ModuleStatesMatch
            };
            return RunSubAudits(courseCode, ops);
        }
    }
}