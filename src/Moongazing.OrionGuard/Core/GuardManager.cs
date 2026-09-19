namespace Moongazing.OrionGuard.Core
{
    /// <summary>
    /// Manages a collection of guard clauses for validation.
    /// </summary>
    public class GuardManager
    {
        private readonly List<IGuardClause> _clauses = new();

        /// <summary>
        /// Adds a new guard clause to the manager.
        /// </summary>
        /// <param name="guardClause">The guard clause to add.</param>
        /// <returns>The current instance of <see cref="GuardManager"/>.</returns>
        public GuardManager AddGuard(IGuardClause guardClause)
        {
            _clauses.Add(guardClause);
            return this;
        }

        /// <summary>
        /// Executes all added guard clauses. If any validation fails, throws an AggregateException.
        /// </summary>
        public void Execute()
        {
            var exceptions = new List<Exception>();

            foreach (var clause in _clauses)
            {
                try
                {
                    clause.Validate();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }

            if (exceptions.Count != 0)
            {
                throw new AggregateException("One or more validations failed.", exceptions);
            }
        }

        /// <summary>
        /// Executes all added guard clauses and returns any exceptions that occurred.
        /// </summary>
        /// <returns>A list of exceptions, or an empty list if all validations passed.</returns>
        public List<Exception> ExecuteWithResults()
        {
            var exceptions = new List<Exception>();

            foreach (var clause in _clauses)
            {
                try
                {
                    clause.Validate();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }

            return exceptions;
        }


    }
}
