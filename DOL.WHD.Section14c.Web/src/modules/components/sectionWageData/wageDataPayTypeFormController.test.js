describe('wageDataPayTypeFormController', function() {

    beforeEach(module('14c'));

    beforeEach(inject(function ($rootScope, $controller) {
        scope = $rootScope.$new();

        wageDataPayTypeFormController = function() {
            return $controller('wageDataPayTypeFormController', {
                '$scope': scope,
                '$route': route
            });
        };
    }));

    // test add/edit/validate/remove a source employer
    it('should add/edit/validate/delete a source employer', function() {
        var controller = wageDataPayTypeFormController();

        controller.activeSourceEmployer = { "employerName": "Employer 1"};
        controller.addSourceEmployer();
        expect(scope.formData[scope.modelPrefix()].mostRecentPrevailingWageSurvey.sourceEmployers.length).toBe(1);

        controller.editSourceEmployer(0);
        controller.addSourceEmployer();
        expect(scope.formData[scope.modelPrefix()].mostRecentPrevailingWageSurvey.sourceEmployers.length).toBe(1);

        controller.validateSourceEmployer(0);

        controller.deleteSourceEmployer(0);
        expect(scope.formData[scope.modelPrefix()].mostRecentPrevailingWageSurvey.sourceEmployers.length).toBe(0);

        controller.cancelAddSourceEmployer();
    });
});
