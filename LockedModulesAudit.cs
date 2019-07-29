using CanvasCourseObjects;
using CanvasCourseObjects.CourseBlueprintSubscription;
using CanvasCourseObjects.CourseModule;
using HttpGrabberFunctions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
namespace LockedModulesAuditor
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
            return JsonConvert.DeserializeObject<List<CanvasModule>>(System.IO.File.ReadAllText($"./output/{id}_modules.json"));
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
        private delegate AuditExecutor AuditFunction(Dictionary<string, CanvasModule> CourseCopy, Dictionary<string, CanvasModule> CourseBlueprint, string ID);
        private List<AuditMessage> RunSubAudits(string courseId, List<AuditFunction> SubAudits)
        {
            List<AuditMessage> auditMessages = new List<AuditMessage>();
            try
            {
                var CourseCopyModules = ConvertListToDictionary(GetModuleFromId(courseId).OrderBy(module => module.Name).ToList());
                var BlueprintModules = ConvertListToDictionary(GetBluePrintCourse(courseId).OrderBy(module => module.Name).ToList());
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

        private void AlignListContent(ref List<CanvasModule> modules)
        {
            modules.OrderBy(module => module.Name);
        }
        private Dictionary<string, CanvasModule> ConvertListToDictionary(List<CanvasModule> courseList)
        {
            return new Dictionary<string, CanvasModule>(
                courseList.OrderBy(module => module.Name).Select(module =>
                {
                    return new KeyValuePair<string, CanvasModule>(module.Id.ToString(), module);
                }
            ));
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
        private string[] CheckPrereqsFromArray(long[] prototype, long[] test, Dictionary<string, CanvasModule> PrototypeModules, Dictionary<string, CanvasModule> testModules)
        {
            List<string> missingModules = new List<string>();
            foreach (var prototypeId in prototype)
            {
                // goes through every id from the prototype and looks for its match in the test array
                if (!Array.Exists(test, testId =>
                    {
                        //because the module IDs will vary by course, we need to look them up by their actual name.

                        CanvasModule testModule = null;
                        if (testModules.ContainsKey(testId.ToString()))
                            testModule = testModules[testId.ToString()];
                        else
                            // we couldn't find the module in the test course
                            return false;

                        CanvasModule prototypeModule = null;
                        if (PrototypeModules.ContainsKey(prototypeId.ToString()))
                            prototypeModule = PrototypeModules[prototypeId.ToString()];
                        else
                            // We couldn't find the module in the prototype course
                            return false;

                        // now that we found each respective module in the courses, we need to make sure they lineup
                        return prototypeModule.Name.Equals(testModule.Name);
                    }))
                {
                    // if we cant find matching modules
                    missingModules.Add(prototypeId.ToString());
                }


            }
            return missingModules.ToArray();
        }

        private AuditExecutor ModulePrereqsMatch(Dictionary<string, CanvasModule> CourseCopy, Dictionary<string, CanvasModule> BlueprintCourse, string ID)
        {
            var AuditMessages = new List<AuditMessage>();
            var successful = true;
            for (var i = 0; i < BlueprintCourse.Count; i++)
            {
                var prototype = BlueprintCourse[BlueprintCourse.Keys.ElementAt(i)];
                var test = CourseCopy[CourseCopy.Keys.ElementAt(i)];

                var missingPrereqs = CheckPrereqsFromArray(prototype.PrerequisiteModuleIds, test.PrerequisiteModuleIds, BlueprintCourse, CourseCopy);
                var extraPrereqs = CheckPrereqsFromArray(test.PrerequisiteModuleIds, prototype.PrerequisiteModuleIds, CourseCopy, BlueprintCourse);
                var failed = !(missingPrereqs.Length <= 0 && extraPrereqs.Length <= 0);
                if (failed)
                {
                    successful = false;
                    var message = "";
                    List<string> warningIds = new List<string>();
                    if (missingPrereqs.Length > 0)
                    {
                        message += $"The Module \"{test.Name}\" is missing the following prerequsites:\n";
                        foreach (string id in missingPrereqs)
                        {
                            if (CourseCopy.ContainsKey(id))
                            {
                                message += (id + " - " + CourseCopy[id].Name);
                            }
                            else
                            {
                                message += id;
                                warningIds.Add(id);
                            }
                            message += "\n";
                        }
                    }
                    // Add the messages for the extra prereqs
                    if (extraPrereqs.Length > 0)
                    {
                        message += $"The Module \"{test.Name}\" hs the following prerequsites which are not in the associated blueprint module:\n";
                        foreach (string id in extraPrereqs)
                        {
                            if (CourseCopy.ContainsKey(id))
                            {
                                message += (id + " - " + CourseCopy[id].Name);
                            }
                            else
                            {
                                message += id;
                                warningIds.Add(id);
                            }
                            message += "\n";
                        }
                    }
                    AuditMessages.Add(GenerateMessage(ID, message, AuditStatus.Fail));
                    if (warningIds.Count > 0)
                    {
                        var warningMessage = $"The following module id{(warningIds.Count > 1 ? "'s have" : " has")} been added to this module as a prerequsite, but {(warningIds.Count > 1 ? "they" : "it")} could not be found in the course:\n";
                        foreach (var warning in warningIds)
                            warningMessage += warning + "\n";
                        AuditMessages.Add(GenerateMessage(ID, warningMessage, AuditStatus.Warn));
                    }
                }

            }
            if (successful)
            {
                AuditMessages.Add(GenerateMessage(ID, "All of the prerequsites of the modules match those in the blueprint", AuditStatus.Pass));
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
        private AuditExecutor ModuleLockDatesMatch(Dictionary<string, CanvasModule> CourseCopy, Dictionary<string, CanvasModule> BlueprintCourse, string ID)
        {
            var AuditMessages = new List<AuditMessage>();
            var successful = true;
            for (var i = 0; i < BlueprintCourse.Count; i++)
            {
                var blueprintModule = BlueprintCourse[BlueprintCourse.Keys.ElementAt(i)];
                var copyModule = CourseCopy[CourseCopy.Keys.ElementAt(i)];

                if (!LockDatesMatch(copyModule, blueprintModule))
                {
                    successful = false;
                    AuditMessages.Add(GenerateMessage(ID, $"The \"{copyModule.Name}\" module has a lockdate which does not match that of the couse blueprint.", AuditStatus.Fail));
                }

            }
            if (successful)
            {
                AuditMessages.Add(GenerateMessage(ID, "All of the modules lock dates match those in the blueprint", AuditStatus.Pass));
            }
            return PipeMessages(AuditMessages);
        }

        private AuditExecutor Template(Dictionary<string, CanvasModule> CourseCopy, Dictionary<string, CanvasModule> BlueprintCourse, string ID)
        {
            var AuditMessages = new List<AuditMessage>();
            return PipeMessages(AuditMessages);
        }

        private bool SequentialProgressConfigurationsMatch(CanvasModule Copy, CanvasModule BlueprintCourse)
        {
            return Copy.RequireSequentialProgress == BlueprintCourse.RequireSequentialProgress;
        }
        private AuditExecutor ModuleSequentialProgressConfigurationsMatch(Dictionary<string, CanvasModule> CourseCopy, Dictionary<string, CanvasModule> BlueprintCourse, string ID)
        {
            var AuditMessages = new List<AuditMessage>();
            var successful = true;
            for (var i = 0; i < BlueprintCourse.Count; i++)
            {
                var blueprintModule = BlueprintCourse[BlueprintCourse.Keys.ElementAt(i)];
                var copyModule = CourseCopy[CourseCopy.Keys.ElementAt(i)];

                if (!SequentialProgressConfigurationsMatch(copyModule, blueprintModule))
                {
                    successful = false;
                    var sequentialProgressRequired = copyModule.RequireSequentialProgress;
                    var required = (sequentialProgressRequired) ? "required" : "not required";
                    var blueprintStatus = (sequentialProgressRequired) ? "does not require" : "does require";
                    AuditMessages.Add(GenerateMessage(ID, $"The \"{copyModule.Name}\" module marks sequential progress as {required} while the course blue print has sequential progress marked as {blueprintStatus} for this module.", AuditStatus.Fail));
                }

            }
            if (successful)
            {
                AuditMessages.Add(GenerateMessage(ID, "All of the modules lock dates match those in the blueprint", AuditStatus.Pass));
            }
            return PipeMessages(AuditMessages);
        }

        private bool StatesMatch(CanvasModule Copy, CanvasModule BlueprintCourse)
        {
            if (Copy.State == null && BlueprintCourse.State == null)
                return true;
            else if (Copy.State == null || BlueprintCourse.State == null)
                return false;
            return Copy.State.Equals(BlueprintCourse.State);
        }
        private AuditExecutor ModuleStatesMatch(Dictionary<string, CanvasModule> CourseCopy, Dictionary<string, CanvasModule> BlueprintCourse, string ID)
        {
             var AuditMessages = new List<AuditMessage>();
            var successful = true;
            for (var i = 0; i < BlueprintCourse.Count; i++)
            {
                var blueprintModule = BlueprintCourse[BlueprintCourse.Keys.ElementAt(i)];
                var copyModule = CourseCopy[CourseCopy.Keys.ElementAt(i)];

                if (!StatesMatch(copyModule, blueprintModule))
                {
                    successful = false;
                    AuditMessages.Add(GenerateMessage(ID, $"The \"{copyModule.Name}\" module marks the state as {copyModule.State} while the course blue print has the state marked as {blueprintModule.State} for this module.", AuditStatus.Fail));
                }

            }
            if (successful)
            {
                AuditMessages.Add(GenerateMessage(ID, "All of the modules lock states match those in the blueprint", AuditStatus.Pass));
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