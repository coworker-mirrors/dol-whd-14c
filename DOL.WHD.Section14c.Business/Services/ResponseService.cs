﻿using System.Collections.Generic;
using System.Linq;
using DOL.WHD.Section14c.DataAccess;
using DOL.WHD.Section14c.Domain.Models.Submission;

namespace DOL.WHD.Section14c.Business.Services
{
    public class ResponseService : IResponseService
    {
        private readonly IResponseRepository _repository;
        public ResponseService(IResponseRepository repository)
        {
            _repository = repository;
        }

        public IEnumerable<Response> GetResponses(string questionKey = null, bool onlyActive = true)
        {
            var responses = _repository.Get();
            if (!string.IsNullOrEmpty(questionKey))
            {
                responses = responses.Where(x => x.QuestionKey == questionKey);
            }
            if (onlyActive)
            {
                responses = responses.Where(x => x.IsActive);
            }

            return responses.ToList();
        }

        public void Dispose()
        {
            _repository.Dispose();
        }
    }
}
