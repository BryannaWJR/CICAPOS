using System.Net.Http;
using CICAPOS.Plugins.Ai;
using Xunit;

public class AiAssistTests
{
    static AiAssistService DemoService() => new(AiProviderConfig.FromSecureConfig("AI_ALLOW_DEMO=true"));

    [Fact]
    public void Prefill_CompanyRegistration_ReturnsBrAndBankFields()
    {
        var res = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.Prefill,
            SchemaKey = AiSchemaKeys.CompanyRegistration
        });
        Assert.True(res.Pass);
        Assert.Equal("demo", res.Source);
        var fields = AiJson.ParseObject(res.FieldsJson);
        Assert.Equal("68899001", fields["br"]);
        Assert.False(string.IsNullOrWhiteSpace(fields["bankAcct"]));
    }

    [Fact]
    public void Prefill_CitfSite_ReturnsContractFields()
    {
        var res = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.Prefill,
            SchemaKey = AiSchemaKeys.CitfSite
        });
        var fields = AiJson.ParseObject(res.FieldsJson);
        Assert.Equal("C/2027/031", fields["no"]);
        Assert.Contains("Tung Chung", fields["n"]);
    }

    [Fact]
    public void Prefill_PalProduct_FillsCoreListingFields()
    {
        var res = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.Prefill,
            SchemaKey = AiSchemaKeys.PalProduct
        });
        var fields = AiJson.ParseObject(res.FieldsJson);
        Assert.Equal("86000", fields["price"]);
        Assert.Contains("SiteEye", fields["name"]);
    }

    [Fact]
    public void MergeFundingIssues_KeepsModelTooBriefAndTooLong()
    {
        var values = new Dictionary<string, string>
        {
            ["objective"] = "在东涌新市镇扩展地盘安装4套AI安全摄像机，覆盖4个塔吊作业区。",
            ["itemsJson"] = "[{\"plan\":\"Cover 4 zones\"}]"
        };
        var brief = AiQuality.MergeFundingIssues(new[]
        {
            new AiIssue { Field = "objective", Code = "too_brief", Message = "Too short", Severity = "error" }
        }, values);
        Assert.Contains(brief, i => i.Code == "too_brief");
        var longOk = AiQuality.MergeFundingIssues(new[]
        {
            new AiIssue { Field = "objective", Code = "too_long", Message = "Too long", Severity = "error" }
        }, values);
        Assert.Contains(longOk, i => i.Code == "too_long");
        var localOnly = AiQuality.ValidateFundingApplication(values);
        Assert.DoesNotContain(localOnly, i => i.Code == "too_brief" || i.Code == "too_long");
    }

    [Fact]
    public void MergeFundingIssues_DropsMissingSchemesWhenAlreadySelected()
    {
        var values = new Dictionary<string, string>
        {
            ["schemes"] = "ACT",
            ["project"] = "Tung Chung New Town Extension – Site 3",
            ["objective"] = "在东涌新市镇扩展地盘安装4套AI安全摄像机，覆盖4个塔吊作业区。",
            ["itemsJson"] = "[]"
        };
        var merged = AiQuality.MergeFundingIssues(new[]
        {
            new AiIssue { Field = "schemes", Code = "missing", Message = "缺少計劃類別", Severity = "error" },
            new AiIssue { Field = "mpdDate", Code = "missing", Message = "MPD date required", Severity = "error" },
            new AiIssue { Field = "objective", Code = "work_setting", Message = "outdoor", Severity = "warn" }
        }, values);
        Assert.DoesNotContain(merged, i => i.Field == "schemes");
        Assert.DoesNotContain(merged, i => i.Field == "mpdDate");
        Assert.Contains(merged, i => i.Code == "work_setting");
    }

    [Fact]
    public void MergeFundingIssues_DropsMismatchThatDemandsUnselectedSchemes()
    {
        var values = new Dictionary<string, string>
        {
            ["schemes"] = "ACT",
            ["objective"] = "在东涌安装4套AI安全摄像机，覆盖4个塔吊作业区。",
            ["itemsJson"] = "[]"
        };
        var merged = AiQuality.MergeFundingIssues(new[]
        {
            new AiIssue
            {
                Field = "objective",
                Code = "scheme_mismatch",
                Message = "項目目標提及安裝AI安全攝像機，屬於ACT範疇，但未有提及BIM、MiC、MiMEP、STB等已選計劃的相關內容。",
                Severity = "error"
            }
        }, values);
        Assert.DoesNotContain(merged, i => i.Code == "scheme_mismatch");
    }

    [Fact]
    public void MergeFundingIssues_DropsActRobotFlaggedAsStb()
    {
        var values = new Dictionary<string, string>
        {
            ["schemes"] = "ACT",
            ["objective"] = "在東涌地盤裙樓3層樓板自動綁扎鋼筋，部署1台機器人。",
            ["itemsJson"] = "[{\"plan\":\"每日覆蓋約800平方米，預計節省約8名扎鐵工人工時\"}]"
        };
        var merged = AiQuality.MergeFundingIssues(new[]
        {
            new AiIssue
            {
                Field = "objective",
                Code = "scheme_mismatch",
                Message = "這是一個具體的實施計劃，但該計劃與ACT無關，因為ACT不涵蓋鋼筋綁扎機器人，這屬於STB範疇。然而，Data.schemes僅包含ACT，因此此計劃不匹配所選計劃，應發出scheme_mismatch。",
                Severity = "error"
            }
        }, values);
        Assert.DoesNotContain(merged, i => i.Code == "scheme_mismatch");
    }

    [Fact]
    public void MergeFundingIssues_DropsWeakContentWhenBenefitAndHowPresent()
    {
        var values = new Dictionary<string, string>
        {
            ["schemes"] = "ACT",
            ["objective"] = "本工程分兩部分：室外在4個塔吊區部署安全攝像機作人機監測；室內在2層機房安裝預制機電模塊，預計室外減少撞擊事故，室內縮短機電安裝約20天。",
            ["itemsJson"] = "[]"
        };
        var merged = AiQuality.MergeFundingIssues(new[]
        {
            new AiIssue { Field = "objective", Code = "weak_content", Message = "The text does not cover expected benefits (AI quality check)", Severity = "error" }
        }, values);
        Assert.DoesNotContain(merged, i => i.Code == "weak_content");
    }

    [Fact]
    public void MergeFundingIssues_KeepsModelPlanQuality()
    {
        var values = new Dictionary<string, string>
        {
            ["objective"] = "Deploy cameras on site to cut struck-by incidents and improve safety for workers during lifting.",
            ["itemsJson"] = "[{\"plan\":\"SiteEye AI Safety Camera System\"}]"
        };
        var model = new[]
        {
            new AiIssue { Field = "item:1.plan", Code = "plan_weak", Message = "Item 1: this does not read as an implementation plan (AI quality check)", Severity = "error" }
        };
        var merged = AiQuality.MergeFundingIssues(model, values);
        Assert.Contains(merged, i => i.Code == "plan_weak");
    }

    [Fact]
    public void MergeFundingIssues_KeepsModelPlanNoQuantity()
    {
        var values = new Dictionary<string, string>
        {
            ["objective"] = "Deploy cameras on site to cut struck-by incidents and improve safety for workers during lifting.",
            ["itemsJson"] = "[{\"plan\":\"Install and monitor plant-person proximity on tower crane exclusion zones across the podium\"}]"
        };
        var model = new[]
        {
            new AiIssue { Field = "item:1.plan", Code = "plan_no_quantity", Message = "Item 1: implementation plan has no quantitative detail (AI quality check)", Severity = "error" }
        };
        var merged = AiQuality.MergeFundingIssues(model, values);
        Assert.Contains(merged, i => i.Code == "plan_no_quantity");
    }

    [Fact]
    public void ValidateForm_AcceptsSeededHarbourObjectiveAndPlans()
    {
        var res = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.ValidateForm,
            SchemaKey = AiSchemaKeys.FundingApplication,
            CurrentValuesJson = "{\"objective\":\"Cut struck-by incidents on four tower-crane zones and automate podium rebar tying to save about 30% of fixing labour.\",\"itemsJson\":\"[{\\\"plan\\\":\\\"Monitor plant-person proximity on 4 tower crane zones.\\\"},{\\\"plan\\\":\\\"Automate rebar tying on 3 podium slabs (local development declaration attached).\\\"}]\"}"
        });
        Assert.True(res.Pass);
        Assert.DoesNotContain(AiJson.ParseIssues(res.IssuesJson), i => i.Severity == "error");
    }

    [Fact]
    public void MergeFundingIssues_DropsHallucinatedPlanMissing()
    {
        var values = new Dictionary<string, string>
        {
            ["objective"] = "Deploy one SiteEye camera on Tung Chung Site 3 to watch 4 tower-crane zones and alert supervisors.",
            ["itemsJson"] = "[{\"plan\":\"Cover 4 tower-crane zones and alert 12 workers.\",\"product\":\"Prefabricated plant-room MEP module\"}]"
        };
        var model = new[]
        {
            new AiIssue { Field = "item:1.plan", Code = "plan_missing", Message = "Item 1: implementation plan is missing", Severity = "error" }
        };
        var merged = AiQuality.MergeFundingIssues(model, values);
        Assert.DoesNotContain(merged, i => i.Code == "plan_missing");
    }

    [Fact]
    public void MergeFundingIssues_DropsHallucinatedSubmitCopy()
    {
        var values = new Dictionary<string, string>
        {
            ["objective"] = "Deploy one SiteEye camera on Tung Chung Site 3 to watch 4 tower-crane zones, alert supervisors within 2 seconds, and cut struck-by incidents for about 40 workers.",
            ["itemsJson"] = "[{\"plan\":\"Cover 4 tower-crane zones and alert 12 workers within 2 seconds of entering the exclusion zone\",\"product\":\"Concrete Curing IoT Sensors (set of 20)\"}]"
        };
        var model = new[]
        {
            new AiIssue { Field = "objective", Code = "", Message = "Objective has fewer than 12 words", Severity = "error" },
            new AiIssue { Field = "objective", Code = "content", Message = "Objective must describe expected benefits and implementation approach.", Severity = "error" },
            new AiIssue { Field = "item:1.plan", Code = "plan_missing", Message = "Item 1: implementation plan is missing", Severity = "error" }
        };
        var merged = AiQuality.MergeFundingIssues(model, values);
        Assert.DoesNotContain(merged, i => i.Code == "plan_missing");
    }

    [Fact]
    public void ValidateForm_RulesDoNotDecideSchemeFit()
    {
        var res = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.ValidateForm,
            SchemaKey = AiSchemaKeys.FundingApplication,
            CurrentValuesJson = "{\"schemes\":\"BIM\",\"objective\":\"Install one PA-ACT-0302 Concrete Curing IoT Sensors set of 20 on Tung Chung Site 3 to monitor slab temperature on 8 pours and cut under-curing rework for about 40 workers.\",\"itemsJson\":\"[]\"}"
        });
        Assert.DoesNotContain(AiJson.ParseIssues(res.IssuesJson), i => i.Code == "scheme_mismatch");
    }

    [Fact]
    public void MergeFundingIssues_KeepsModelSchemeJudgment()
    {
        var values = new Dictionary<string, string>
        {
            ["schemes"] = "BIM",
            ["objective"] = "Install one PA-ACT-0302 Concrete Curing IoT Sensors set of 20 on Tung Chung Site 3 to monitor slab temperature on 8 pours and cut under-curing rework for about 40 workers.",
            ["itemsJson"] = "[]"
        };
        var model = new[]
        {
            new AiIssue
            {
                Field = "objective",
                Code = "scheme_mismatch",
                Message = "The text describes concrete-curing sensors, which belong to ACT, not the selected BIM scheme.",
                Severity = "error"
            }
        };
        var merged = AiQuality.MergeFundingIssues(model, values);
        Assert.Contains(merged, i => i.Code == "scheme_mismatch" && i.Message.Contains("not the selected BIM scheme"));
    }

    [Fact]
    public void ApplyWorkSetting_OutdoorProject_FlagsIndoorEquipment()
    {
        var values = new Dictionary<string, string>
        {
            ["project"] = "Tung Chung New Town Extension – Site 3",
            ["schemes"] = "MiMEP",
            ["objective"] = "Install modules for outdoor civil works on an open site with tower-crane zones.",
            ["itemsJson"] = "[{\"product\":\"Prefabricated plant-room MEP module\",\"plan\":\"One plant room\",\"type\":\"PA\",\"paCode\":\"PA-MEP-0201\"}]"
        };
        var fields = new Dictionary<string, string> { ["summary"] = "Applicant submitted a MiMEP application.", ["risksJson"] = "[]" };
        AiQuality.ApplyWorkSetting(fields, values);
        Assert.Contains("Project setting is outdoor", fields["summary"]);
        Assert.Contains("do not all suit outdoor use", fields["summary"]);
        Assert.Equal("[]", fields["risksJson"]);
        Assert.Equal("outdoor", fields["setting"]);
    }

    [Fact]
    public void ApplyWorkSetting_IndoorProject_FlagsDrone()
    {
        var values = new Dictionary<string, string>
        {
            ["project"] = "Central Plaza office fit-out",
            ["schemes"] = "ACT",
            ["objective"] = "Improve indoor air checks for the fit-out floors.",
            ["itemsJson"] = "[{\"product\":\"DroneScan Site Survey Kit\",\"plan\":\"Weekly drone flights\",\"type\":\"PA\",\"paCode\":\"PA-ACT-0377\"}]"
        };
        var fields = new Dictionary<string, string> { ["summary"] = "Applicant submitted an ACT application.", ["risksJson"] = "[]" };
        AiQuality.ApplyWorkSetting(fields, values);
        Assert.Contains("Project setting is indoor", fields["summary"]);
        Assert.Equal("[]", fields["risksJson"]);
    }

    [Fact]
    public void ApplyWorkSetting_OutdoorProject_AcceptsDrone()
    {
        var values = new Dictionary<string, string>
        {
            ["project"] = "Tung Chung Site 3",
            ["schemes"] = "ACT",
            ["objective"] = "Survey the excavation.",
            ["itemsJson"] = "[{\"product\":\"DroneScan Site Survey Kit\",\"plan\":\"Weekly drone flights over 4 zones\",\"type\":\"PA\",\"paCode\":\"PA-ACT-0377\"}]"
        };
        var fields = new Dictionary<string, string> { ["summary"] = "Applicant submitted an ACT application.", ["risksJson"] = "[]" };
        AiQuality.ApplyWorkSetting(fields, values);
        Assert.Contains("Requested items suit outdoor use", fields["summary"]);
        Assert.DoesNotContain("does not meet", fields["risksJson"]);
    }

    [Fact]
    public void ApplyWorkSetting_IgnoresProjectTitle_WhenObjectiveHasNoSetting()
    {
        var values = new Dictionary<string, string>
        {
            ["project"] = "Tung Chung New Town Extension – Site 3",
            ["schemes"] = "ACT",
            ["objective"] = "We will adopt new technology to improve productivity and train the site team.",
            ["itemsJson"] = "[{\"product\":\"Concrete Curing IoT Sensors\",\"type\":\"PA\",\"paCode\":\"PA-ACT-0302\",\"plan\":\"Deploy 2 units from week 3\"}]"
        };
        var fields = new Dictionary<string, string>
        {
            ["summary"] = "The work setting is outdoor, as evidenced by the objective stating 'outdoor civil works on an open site'.",
            ["risksJson"] = "[]"
        };
        AiQuality.ApplyWorkSetting(fields, values);
        Assert.Equal("unspecified", fields["setting"]);
        Assert.Contains("unspecified", fields["summary"], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("New Town", fields["summary"]);
        Assert.Contains("unspecified", fields["risksJson"], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateForm_ActCuringTextMatchesActScheme()
    {
        var res = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.ValidateForm,
            SchemaKey = AiSchemaKeys.FundingApplication,
            CurrentValuesJson = "{\"schemes\":\"ACT\",\"objective\":\"Install one PA-ACT-0302 Concrete Curing IoT Sensors set of 20 on Tung Chung Site 3 to monitor slab temperature on 8 pours and cut under-curing rework for about 40 workers.\",\"itemsJson\":\"[]\"}"
        });
        Assert.DoesNotContain(AiJson.ParseIssues(res.IssuesJson), i => i.Code == "scheme_mismatch");
        Assert.DoesNotContain(AiJson.ParseIssues(res.IssuesJson), i => i.Code == "weak_content");
    }

    [Fact]
    public void ValidateUpload_QuotationMismatch_FlagsAmount()
    {
        var res = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.ValidateUpload,
            SchemaKey = AiSchemaKeys.QuotationCheck,
            DocType = "npa",
            CurrentValuesJson = "{\"formAmount\":\"420000\"}"
        });
        Assert.False(res.Pass);
        Assert.Contains(AiJson.ParseIssues(res.IssuesJson), i => i.Code == "mismatch");
    }

    [Fact]
    public void ValidateUpload_SwappedNar1AndBr_FlagsWrongType()
    {
        var brInNar1 = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.ValidateUpload,
            SchemaKey = AiSchemaKeys.SupportingDoc,
            DocType = "NAR1 (Annual Return)",
            FileName = "BR_Harbour_Holdings_50112233.pdf"
        });
        Assert.False(brInNar1.Pass);
        Assert.Contains(AiJson.ParseIssues(brInNar1.IssuesJson), i => i.Code == "wrong_type");

        var nar1InBr = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.ValidateUpload,
            SchemaKey = AiSchemaKeys.SupportingDoc,
            DocType = "Shareholder BR",
            FileName = "NAR1_Harbour_Build_2026.pdf"
        });
        Assert.False(nar1InBr.Pass);
        Assert.Contains(AiJson.ParseIssues(nar1InBr.IssuesJson), i => i.Code == "wrong_type");
    }

    [Fact]
    public void ValidateUpload_UnsignedGeneratedForm_FlagsSignatureAndChop()
    {
        var res = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.ValidateUpload,
            SchemaKey = AiSchemaKeys.SupportingDoc,
            DocType = "signed_application_form",
            FileName = "CITF-2027-00098-ApplicationForm.pdf",
            DocumentText = "CITF-2027-00098 Harbour Build Limited GRANT 29400 [[UNSIGNED-SIGNATURE]] [[UNSIGNED-CHOP]]",
            CurrentValuesJson = "{\"ref\":\"CITF-2027-00098\",\"company\":\"Harbour Build Limited\",\"grant\":\"29400\"}"
        });
        Assert.False(res.Pass);
        Assert.Contains(AiJson.ParseIssues(res.IssuesJson), i => i.Code == "missing_signature");
        Assert.Contains(AiJson.ParseIssues(res.IssuesJson), i => i.Code == "missing_chop");
    }

    static byte[] MakeApplicationFormPdf(bool signed)
    {
        var lines = new[]
        {
            "CONSTRUCTION INNOVATION AND TECHNOLOGY FUND - APPLICATION FORM",
            "Form ID: CITF-2027-00021",
            "Applicant: Harbour Build Engineering Limited (BR 61234567)",
            "Grant requested: HKD 29400",
            signed ? "[[SIGNED]] signed by management representative  [[CHOPPED]] company stamp applied" : "[[UNSIGNED-SIGNATURE]] Signature: ________________",
            signed ? "Signature and company chop present." : "[[UNSIGNED-CHOP]] Company chop: ________________"
        };
        static string Esc(string s) => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        var y = 800;
        var cmds = new System.Text.StringBuilder("BT\n/F1 10 Tf");
        foreach (var line in lines)
        {
            cmds.Append("\n1 0 0 1 40 ").Append(y).Append(" Tm (").Append(Esc(line)).Append(") Tj");
            y -= 14;
        }
        cmds.Append("\nET");
        var stream = cmds.ToString();
        var objects = new[]
        {
            "1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n",
            "2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj\n",
            "3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >> endobj\n",
            "4 0 obj << /Length " + stream.Length + " >> stream\n" + stream + "\nendstream endobj\n",
            "5 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Courier >> endobj\n"
        };
        var outPdf = new System.Text.StringBuilder("%PDF-1.4\n");
        var xref = new List<int> { 0 };
        foreach (var o in objects)
        {
            xref.Add(outPdf.Length);
            outPdf.Append(o);
        }
        var start = outPdf.Length;
        outPdf.Append("xref\n0 ").Append(xref.Count).Append("\n0000000000 65535 f \n");
        for (var i = 1; i < xref.Count; i++)
            outPdf.Append(xref[i].ToString("D10")).Append(" 00000 n \n");
        outPdf.Append("trailer << /Size ").Append(xref.Count).Append(" /Root 1 0 R >>\nstartxref\n").Append(start).Append("\n%%EOF\n");
        return System.Text.Encoding.Latin1.GetBytes(outPdf.ToString());
    }

    [Fact]
    public void ValidateUpload_SignedPdfBytes_WithoutDocumentText_Passes()
    {
        var res = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.ValidateUpload,
            SchemaKey = AiSchemaKeys.SupportingDoc,
            DocType = "signed_application_form",
            FileName = "CITF-2027-00021-ApplicationForm-signed.pdf",
            FileBase64 = Convert.ToBase64String(MakeApplicationFormPdf(true)),
            CurrentValuesJson = "{\"ref\":\"CITF-2027-00021\",\"company\":\"Harbour Build Engineering Limited\",\"grant\":\"29400\"}"
        });
        Assert.True(res.Pass, res.Notes);
        Assert.Contains("[[SIGNED]]", PdfText.Extract(MakeApplicationFormPdf(true)));
    }

    [Fact]
    public void ValidateUpload_UnsignedPdfBytes_WithoutDocumentText_FlagsSignature()
    {
        var res = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.ValidateUpload,
            SchemaKey = AiSchemaKeys.SupportingDoc,
            DocType = "signed_application_form",
            FileName = "CITF-2027-00021-ApplicationForm.pdf",
            FileBase64 = Convert.ToBase64String(MakeApplicationFormPdf(false)),
            CurrentValuesJson = "{\"ref\":\"CITF-2027-00021\",\"company\":\"Harbour Build Engineering Limited\",\"grant\":\"29400\"}"
        });
        Assert.False(res.Pass);
        Assert.Contains(AiJson.ParseIssues(res.IssuesJson), i => i.Code == "missing_signature");
    }

    [Fact]
    public void ValidateUpload_SignedMatchingForm_Passes()
    {
        var res = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.ValidateUpload,
            SchemaKey = AiSchemaKeys.SupportingDoc,
            DocType = "signed_application_form",
            FileName = "CITF-2027-00098-ApplicationForm-signed.pdf",
            DocumentText = "CITF-2027-00098 Harbour Build Limited grant 29400 [[SIGNED]] by Lau Wing Kei [[CHOPPED]] company stamp applied",
            CurrentValuesJson = "{\"ref\":\"CITF-2027-00098\",\"company\":\"Harbour Build Limited\",\"grant\":\"29400\"}"
        });
        Assert.True(res.Pass);
    }

    [Fact]
    public void ValidateUpload_Ocr429_FilenameMatchesSlot_Passes()
    {
        var svc = new AiAssistService(
            AiProviderConfig.FromSecureConfig("AI_ALLOW_DEMO=true;NOVA_OCR_API_KEY=sk-test"),
            ocr: new BusyOcr());
        var res = svc.Execute(new AiAssistRequest
        {
            Action = AiActions.ValidateUpload,
            SchemaKey = AiSchemaKeys.SupportingDoc,
            DocType = "NAR1 (Annual Return)",
            FileName = "NAR1_Harbour_Build_2026.pdf",
            FileBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5 })
        });
        Assert.True(res.Pass, res.Notes);
        Assert.Equal("ocr-busy", res.Source);
    }

    [Fact]
    public void ValidateUpload_MatchingNar1File_PassesType()
    {
        var res = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.ValidateUpload,
            SchemaKey = AiSchemaKeys.SupportingDoc,
            DocType = "NAR1 (Annual Return)",
            FileName = "NAR1_Harbour_Build_2026.pdf"
        });
        Assert.DoesNotContain(AiJson.ParseIssues(res.IssuesJson), i => i.Code == "wrong_type");
    }

    [Fact]
    public void ValidateUpload_BlurredScan_FlagsReadable()
    {
        var res = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.ValidateUpload,
            SchemaKey = AiSchemaKeys.SupportingDoc,
            DocType = "blurred"
        });
        Assert.False(res.Pass);
        Assert.Contains(AiJson.ParseIssues(res.IssuesJson), i => i.Code == "blurred");
    }

    [Fact]
    public void ValidateUpload_BlurredScan_LiveConfigDoesNotRequireFile()
    {
        var svc = new AiAssistService(AiProviderConfig.FromSecureConfig("AI_ALLOW_DEMO=false;NOVA_API_KEY=ak-test"));
        var res = svc.Execute(new AiAssistRequest
        {
            Action = AiActions.ValidateUpload,
            SchemaKey = AiSchemaKeys.SupportingDoc,
            DocType = "blurred"
        });
        Assert.False(res.Pass);
        Assert.Contains(AiJson.ParseIssues(res.IssuesJson), i => i.Code == "blurred");
    }

    [Fact]
    public void AllSchemas_AreRegistered()
    {
        Assert.Contains(AiFormSchemas.List(), s => s.Key == AiSchemaKeys.CompanyRegistration);
        Assert.Contains(AiFormSchemas.List(), s => s.Key == AiSchemaKeys.PalProduct);
        Assert.Contains(AiFormSchemas.List(), s => s.Key == AiSchemaKeys.FundingApplication);
        Assert.Contains(AiFormSchemas.List(), s => s.Key == AiSchemaKeys.DisbursementRequest);
        Assert.Contains(AiFormSchemas.List(), s => s.Key == AiSchemaKeys.AnnualConfirmation);
        Assert.Equal(11, AiFormSchemas.List().Count());
    }

    [Fact]
    public void Config_HasOcr_WhenNovaOcrKeyPresent()
    {
        var cfg = AiProviderConfig.FromSecureConfig("NOVA_OCR_API_KEY=sk-test-ocr");
        Assert.True(cfg.HasOcr);
        Assert.Equal(AiProviderConfig.DefaultNovaOcrBase, cfg.OcrBaseUrl);
    }

    [Fact]
    public void Prefill_WithOcrAndLlm_MapsDocumentToFields()
    {
        var cfg = new AiProviderConfig
        {
            CompatibleBaseUrl = "https://example.test/v1",
            CompatibleApiKey = "ak-test",
            CompatibleModel = "DeepSeek-V4-Flash",
            OcrApiKey = "sk-test"
        };
        var ocr = new FakeOcr("Business Registration No. 68899001\nCompany: Lantau Smart Construction Limited");
        var llm = new FakeLlm("{\"br\":\"68899001\",\"nameEn\":\"Lantau Smart Construction Limited\",\"bankAcct\":\"511-278830-838\"}");
        var svc = new AiAssistService(cfg, llm, ocr);
        var res = svc.Execute(new AiAssistRequest
        {
            Action = AiActions.Prefill,
            SchemaKey = AiSchemaKeys.CompanyRegistration,
            FileName = "BR.pdf",
            FileBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 })
        });
        Assert.Equal("nova-ocr+llm", res.Source);
        Assert.Equal("DeepSeek-V4-Flash", res.Model);
        var fields = AiJson.ParseObject(res.FieldsJson);
        Assert.Equal("68899001", fields["br"]);
        Assert.True(ocr.Called);
        Assert.True(llm.Called);
    }

    [Fact]
    public void Prefill_FileWithoutOcrKey_FallsBackToDemo_OnlyWhenAllowed()
    {
        var res = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.Prefill,
            SchemaKey = AiSchemaKeys.CitfSite,
            FileName = "award.pdf",
            FileBase64 = Convert.ToBase64String(new byte[] { 9, 9, 9 })
        });
        Assert.Equal("demo", res.Source);
        Assert.Contains("NOVA_OCR_API_KEY", res.Notes ?? "");
    }

    [Fact]
    public void Prefill_FileWithoutOcrKey_ThrowsWhenLiveRequired()
    {
        var cfg = new AiProviderConfig
        {
            CompatibleBaseUrl = "https://example.test/v1",
            CompatibleApiKey = "ak-test",
            AllowDemo = false
        };
        var svc = new AiAssistService(cfg, new FakeLlm("{}"), ocr: null);
        Assert.Throws<InvalidOperationException>(() => svc.Execute(new AiAssistRequest
        {
            Action = AiActions.Prefill,
            SchemaKey = AiSchemaKeys.CitfSite,
            FileName = "award.pdf",
            FileBase64 = Convert.ToBase64String(new byte[] { 9, 9, 9 })
        }));
    }

    [Fact]
    public void Ocr_ParsesNestedTaskIdThenPollsText()
    {
        var n = 0;
        byte[] Send(HttpRequestMessage req)
        {
            n++;
            if (n == 1) return System.Text.Encoding.UTF8.GetBytes("{\"code\":0,\"data\":{\"taskId\":\"t-99\"}}");
            return System.Text.Encoding.UTF8.GetBytes("{\"status\":\"success\",\"content\":\"Business Registration No. 68899001 extra sample text.\"}");
        }
        var client = new NovaOcrClient(new AiProviderConfig { OcrApiKey = "sk-test" }, Send);
        var text = client.ExtractText(new byte[] { 1, 2, 3, 4 }, "br.pdf");
        Assert.Contains("68899001", text);
        Assert.Equal(2, n);
    }

    [Fact]
    public void Ocr_ExtractsTaskIdFromNestedJson()
    {
        Assert.Equal("t-99", NovaOcrClient.TryExtractId("{\"code\":0,\"data\":{\"taskId\":\"t-99\"}}"));
    }

    [Fact]
    public void LiveConfig_DisallowsDemoByDefault()
    {
        var cfg = AiProviderConfig.FromSecureConfig(null);
        Assert.False(cfg.AllowDemo);
        Assert.Equal("unconfigured", cfg.ActiveProvider);
    }

    [Fact]
    public void ValidateUpload_PalMatrixDoc_ReturnsOcrNote()
    {
        var res = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.ValidateUpload,
            SchemaKey = AiSchemaKeys.SupportingDoc,
            DocType = "Authorisation letter"
        });
        Assert.True(res.Pass);
        var fields = AiJson.ParseObject(res.FieldsJson);
        Assert.Contains("OCR", fields["note"]);
    }

    [Fact]
    public void ValidateForm_Disbursement_FlagsMissingDocs()
    {
        var res = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.ValidateForm,
            SchemaKey = AiSchemaKeys.DisbursementRequest,
            CurrentValuesJson = "{\"docsJson\":\"[]\",\"paid\":\"true\"}"
        });
        Assert.False(res.Pass);
        Assert.Contains(AiJson.ParseIssues(res.IssuesJson), i => i.Code == "missing");
    }

    [Fact]
    public void ValidateForm_AnnualConfirmation_AcceptsCompleteSurvey()
    {
        var res = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.ValidateForm,
            SchemaKey = AiSchemaKeys.AnnualConfirmation,
            CurrentValuesJson = "{\"hasNc\":\"No\",\"prod\":\"4\",\"safety\":\"5\",\"quality\":\"3\"}"
        });
        Assert.True(res.Pass);
    }

    [Fact]
    public void Prefill_FromProductWebpage_MapsListingFields()
    {
        var cfg = new AiProviderConfig
        {
            CompatibleBaseUrl = "https://example.test/v1",
            CompatibleApiKey = "ak-test",
            CompatibleModel = "DeepSeek-V4-Flash"
        };
        var llm = new FakeLlm("{\"name\":\"SiteEye AI Safety Camera\",\"model\":\"SE-400\",\"spec\":\"IP66 edge AI camera\",\"mf\":\"SmartSite\",\"price\":\"86000\"}");
        var web = new FakeWeb("SiteEye AI Safety Camera SE-400 IP66 helmet detection HK$86,000 manufacturer SmartSite Robotics.");
        var svc = new AiAssistService(cfg, llm, ocr: null, web: web);
        var res = svc.Execute(new AiAssistRequest
        {
            Action = AiActions.Prefill,
            SchemaKey = AiSchemaKeys.PalProduct,
            CurrentValuesJson = "{\"pageUrl\":\"https://smartsite.example.com/se-400\"}"
        });
        Assert.Contains("Webpage", res.Notes ?? "");
        Assert.Equal("SiteEye AI Safety Camera", AiJson.ParseObject(res.FieldsJson)["name"]);
        Assert.True(web.Called);
        Assert.True(llm.Called);
    }

    [Fact]
    public void ApplyPalCompare_FlagsCloseListedProduct()
    {
        var values = new Dictionary<string, string>
        {
            ["project"] = "Tung Chung Site 3",
            ["objective"] = "Cut struck-by incidents on four tower-crane zones with cameras.",
            ["itemsJson"] = "[{\"product\":\"SiteEye AI Safety Camera System\",\"type\":\"NPA\",\"unit\":\"92000\",\"functions\":\"24/7 video analytics for PPE and exclusion zones\"}]",
            ["palJson"] = "[{\"code\":\"PA-ACT-0412\",\"name\":\"SiteEye AI Safety Camera System (4S)\",\"scheme\":\"ACT\",\"price\":\"86000\",\"functions\":\"24/7 site video analytics: detects missing PPE\"}]"
        };
        var fields = new Dictionary<string, string> { ["summary"] = "Applicant submitted an ACT application.", ["risksJson"] = "[]" };
        AiQuality.ApplyPalCompare(fields, values);
        Assert.Contains("Function and price comparison", fields["summary"], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PA-ACT-0412", fields["summary"]);
        Assert.Contains("HK$92000", fields["summary"]);
        Assert.Contains("HK$86000", fields["summary"]);
        Assert.Contains("more cost-effective", fields["summary"], StringComparison.OrdinalIgnoreCase);
        Assert.True(
            fields["summary"].IndexOf("better match", StringComparison.OrdinalIgnoreCase) >= 0
            || fields["summary"].IndexOf("similar for this project", StringComparison.OrdinalIgnoreCase) >= 0);
        Assert.Contains("\"severity\":\"tip\"", fields["risksJson"]);
        Assert.DoesNotContain("\"severity\":\"warn\"", fields["risksJson"]);
    }

    [Fact]
    public void Summarize_Demo_ReturnsSubmissionSummary()
    {
        var res = DemoService().Execute(new AiAssistRequest
        {
            Action = AiActions.Summarize,
            SchemaKey = AiSchemaKeys.FundingApplication,
            CurrentValuesJson = "{\"project\":\"Tung Chung Site 3\",\"schemes\":\"ACT\",\"objective\":\"Cut struck-by incidents on four tower-crane zones and automate podium rebar tying.\",\"itemsJson\":\"[{\\\"product\\\":\\\"SiteEye\\\"}]\",\"amount\":\"764000\"}"
        });
        Assert.True(res.Pass);
        var fields = AiJson.ParseObject(res.FieldsJson);
        Assert.False(string.IsNullOrWhiteSpace(fields["summary"]));
        Assert.Contains("ACT", fields["summary"]);
        Assert.DoesNotContain("{", fields["summary"]);
        Assert.DoesNotContain("[", fields["summary"]);
        Assert.Contains("SiteEye", fields["summary"]);
    }

    [Fact]
    public void SanitizeSummaryText_StripsJsonDumps()
    {
        var raw = "1. Applicant submitted an ACT funding application.\n"
            + "2. Objective (98 words) describes implementation. Items: [{\"plan\":\"Mount 1 camera\",\"product\":\"Concrete Curing IoT Sensors\",\"type\":\"PA\",\"paCode\":\"PA-ACT-0302\"}].\n"
            + "3. for curing control. Amount applied HK$361200.\n"
            + "Tip: Function and price comparison: no alternative is needed.";
        var clean = AiQuality.SanitizeSummaryText(raw);
        Assert.DoesNotContain("{", clean);
        Assert.DoesNotContain("[", clean);
        Assert.DoesNotContain("paCode", clean);
        var fields = new Dictionary<string, string> { ["summary"] = clean, ["risksJson"] = "[{\"severity\":\"tip\",\"text\":\"Function and price comparison: no alternative is needed.\"}]" };
        var values = new Dictionary<string, string>
        {
            ["itemsJson"] = "[{\"product\":\"Concrete Curing IoT Sensors\",\"type\":\"PA\",\"paCode\":\"PA-ACT-0302\"}]"
        };
        AiQuality.ScrubPalCompareIfNoNpa(fields, values);
        Assert.DoesNotContain("Function and price", fields["summary"]);
        Assert.DoesNotContain("no alternative", fields["summary"]);
        Assert.DoesNotContain("Function and price", fields["risksJson"]);
    }

    [Fact]
    public void ApplyPalCompare_PaOnly_DoesNotCompare()
    {
        var values = new Dictionary<string, string>
        {
            ["itemsJson"] = "[{\"product\":\"Concrete Curing IoT Sensors\",\"type\":\"PA\",\"paCode\":\"PA-ACT-0302\",\"unit\":\"42000\"}]",
            ["palJson"] = "[{\"code\":\"PA-ACT-0302\",\"name\":\"Concrete Curing IoT Sensors\",\"price\":\"42000\",\"functions\":\"wireless sensors\"}]"
        };
        var fields = new Dictionary<string, string> { ["summary"] = "Applicant submitted an ACT application.", ["risksJson"] = "[]" };
        AiQuality.ApplyPalCompare(fields, values);
        Assert.DoesNotContain("Function and price comparison", fields["summary"]);
        Assert.Equal("[]", fields["risksJson"]);
    }

    sealed class FakeWeb : IWebPageClient
    {
        readonly string _text;
        public bool Called { get; private set; }
        public FakeWeb(string text) => _text = text;
        public string FetchText(string url)
        {
            Called = true;
            return _text;
        }
    }

    sealed class BusyOcr : INovaOcrClient
    {
        public string ExtractText(byte[] fileBytes, string? fileName)
            => throw new InvalidOperationException("D.Nova OCR rejected request (code 429).");
    }

    [Fact]
    public void ToTraditional_RewritesSimplifiedAndLeavesTraditional()
    {
        Assert.Equal("項目目標與質量檢查", ChineseScript.ToTraditional("项目目标与质量检查"));
        Assert.Equal("工程屬室外，所申請物品符合室外使用需要。", ChineseScript.ToTraditional("工程屬室外，所申請物品符合室外使用需要。"));
    }

    sealed class FakeOcr : INovaOcrClient
    {
        readonly string _text;
        public bool Called { get; private set; }
        public FakeOcr(string text) => _text = text;
        public string ExtractText(byte[] fileBytes, string? fileName)
        {
            Called = true;
            return _text;
        }
    }

    sealed class FakeLlm : ILlmClient
    {
        readonly string _json;
        public bool Called { get; private set; }
        public FakeLlm(string json) => _json = json;
        public string CompleteJson(string systemPrompt, string userPrompt)
        {
            Called = true;
            return _json;
        }
    }
}
