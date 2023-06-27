using Hl7.Fhir.FhirPath.Validator;
using Hl7.Fhir.Introspection;
using Hl7.Fhir.Language;
using Hl7.Fhir.Utility;
using Hl7.FhirPath.Expressions;
using Hl7.FhirPath.Sprache;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FhirPathLab_DotNetEngine
{
    public class JsonExpressionTreeVisitor : BaseFhirPathExpressionVisitor
    {
        public JsonExpressionTreeVisitor(ModelInspector mi, List<string> SupportedResources, Type[] OpenTypes)
            : base(mi, SupportedResources, OpenTypes)
        {

        }
        private readonly StringBuilder _result = new StringBuilder();
        private int _indent = 0;
        private Stack<JsonRep> _stack = new Stack<JsonRep>();

        public override FhirPathVisitorProps VisitConstant(ConstantExpression expression)
        {
            JsonRep r = new JsonRep()
            {
                ExpressionType = expression.GetType().Name,
                Name = expression.Value.ToString(),
            };
            if (!_stack.Any())
                _stack.Push(r); // this is then likely the only property
            else
                _stack.Peek().Arguments.Add(r);
            var result = base.VisitConstant(expression);
            r.ReturnType = result.ToString();

            return result;
        }

        public override FhirPathVisitorProps VisitFunctionCall(FunctionCallExpression expression)
        {
            JsonRep r = new JsonRep()
            {
                ExpressionType = expression.GetType().Name,
                Name = expression.FunctionName.Replace("binary.",""),
            };
            if (expression is ChildExpression ce)
                r.Name = ce.ChildName;
            if (_stack.Any())
                _stack.Peek().Arguments.Add(r);
            _stack.Push(r);
            var result = base.VisitFunctionCall(expression);
            r.ReturnType = result.ToString();
            if (_stack.Count > 1)
                _stack.Pop();

            return result;
        }

        public override FhirPathVisitorProps VisitNewNodeListInit(NewNodeListInitExpression expression)
        {
            var result = base.VisitNewNodeListInit(expression);
            //JsonRep r = new JsonRep()
            //{
            //    ExpressionType = expression.GetType().Name,
            //    Name = expression.Name,
            //    ReturnType = result.ToString(),
            //};
            // foreach (var element in expression.Contents)
            //    element.Accept(this);

            return result;
        }

        public override FhirPathVisitorProps VisitVariableRef(VariableRefExpression expression)
        {
            var result = base.VisitVariableRef(expression);
            JsonRep r = new JsonRep()
            {
                ExpressionType = expression.GetType().Name,
                Name = expression.Name,
                ReturnType = result.ToString(),
            };
            if (!_stack.Any())
                _stack.Push(r); // this is then likely the only property
            else
                _stack.Peek().Arguments.Add(r);

            return result;
        }

        public JsonRep ToJson()
        {
            if (_stack.Any())
                return _stack.Pop();
            return new JsonRep();
        }
    }

    public class JsonRep
    {
        public string ExpressionType { get; set; }
        public string Name { get; set; }
        public List<JsonRep> Arguments { get; private set; } = new List<JsonRep>();
        public string ReturnType { get; set; }
    }

    public static class JsonVisualizerExpressionExtensions
    {
        public static JsonRep ToJson(this Expression expr)
        {
            var dumper = new JsonExpressionTreeVisitor(
                ModelInspector.ForAssembly(typeof(Hl7.Fhir.Model.Patient).Assembly),
                Hl7.Fhir.Model.ModelInfo.SupportedResources,
                Hl7.Fhir.Model.ModelInfo.OpenTypes);
            var result = expr.Accept(dumper);
            return dumper.ToJson();
        }
    }

}
