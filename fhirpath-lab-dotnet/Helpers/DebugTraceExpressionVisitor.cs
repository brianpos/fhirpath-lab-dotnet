using Hl7.FhirPath.Expressions;
using System.Linq;

namespace FhirPathLab_DotNetEngine
{
	/// <summary>
	/// Visitor that clones an expression tree and injects a Custom Trace
	/// function for debugging around all function calls.
	/// This is then used to stash all the intermediate values retrieved during processing
	/// to create a form of debug tracing.
	/// </summary>
	public class DebugTraceExpressionVisitor : ExpressionVisitor<Expression>
	{
		public DebugTraceExpressionVisitor()
		{
		}


		public override Expression VisitConstant(ConstantExpression expression)
		{
			// constants don't need cloning, as they are immutable
			// and have no arguments or children
			return WrapWithDebugTrace(expression, "constant");
		}

		public override Expression VisitFunctionCall(FunctionCallExpression expression)
		{
			FunctionCallExpression newCall;
			string name;
			// Create the correct derived types
			switch (expression)
			{
				case ChildExpression ce:
					var focusChild = expression.Focus.Accept(this); // clone the focus expression
					name = ce.ChildName;
					newCall = new ChildExpression(
						focusChild,
						ce.ChildName,
						expression.Location);
					break;
				case IndexerExpression ie:
					var focusIndex = expression.Focus.Accept(this); // clone the focus expression
					name = "[]";
					newCall = new IndexerExpression(
						focusIndex,
						ie.Index.Accept(this),
						ie.LeftBrace,
						ie.RightBrace,
						expression.Location);
					break;
				case BinaryExpression be:
					name = expression.FunctionName.Replace("binary.","");
					newCall = new BinaryExpression(
						be.OpToken,
						be.Arguments.First().Accept(this),
						be.Arguments.Skip(1).First().Accept(this),
						be.Location
						);
					break;
				case UnaryExpression ue:
					name = ue.Op;
					newCall = new UnaryExpression(
						ue.Op, ue.Operand.Accept(this), ue.Location);
					break;
				default:
					var focus = expression.Focus.Accept(this); // clone the focus expression
					name = expression.FunctionName;
					var newArgs = expression.Arguments.Select(arg => arg.Accept(this)).ToList(); // clone each argument expression
					newCall = new FunctionCallExpression(
						focus,
						expression.FunctionName,
						expression.LeftBrace, // added left brace
						expression.RightBrace, // added right brace
						expression.ExpressionType, // added type
						newArgs,
						expression.Location);
					break;
			}


			if (newCall.FunctionName == "trace")
				return newCall; // return the newly created function call

			return WrapWithDebugTrace(newCall, name);
		}

		private static Expression WrapWithDebugTrace(Expression expression, string name)
		{
			string location = $"{expression.Location.LineNumber}.{expression.Location.LinePosition}";
			if (expression.Location is FhirPathExpressionLocationInfo loc)
			{
				location = $"{loc.RawPosition},{loc.Length},{name}";
			}
			// Wrap into a trace function call
			var traceCall = new FunctionCallExpression(
				expression,
				"debugTrace",
				new SubToken('('),
				new SubToken(')'),
				expression.ExpressionType,
				new ConstantExpression(location),
				AxisExpression.This,
				AxisExpression.Index
				);

			return traceCall; // return the newly created function call
		}

		public override Expression VisitNewNodeListInit(NewNodeListInitExpression expression)
		{
			// constants don't need cloning, as they are immutable
			// and have no arguments or children
			return expression;
		}

		public override Expression VisitVariableRef(VariableRefExpression expression)
		{
			// constants don't need cloning, as they are immutable
			// and have no arguments or children
			if (expression is AxisExpression)
				return expression;
			return WrapWithDebugTrace(expression, expression.Name);
		}

		public override Expression VisitCustomExpression(CustomExpression expression)
		{
			if (expression is BracketExpression br)
			{
				var newBr = new BracketExpression(br.LeftBrace, br.RightBrace, br.Operand.Accept(this), br.Location);
				return newBr;
			}
			return base.VisitCustomExpression(expression);
		}
	}

}
